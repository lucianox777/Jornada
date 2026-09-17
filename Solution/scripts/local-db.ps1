param(
    [ValidateSet('up','reset','down','clean','status','backfill')]
    [string]$Action = 'up',
    [switch]$NoSyntheticCorpus
)
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
$Example = Join-Path $Root '.env.example'
New-Item -ItemType Directory -Force (Join-Path $Root '.local/sql-backup') | Out-Null

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw "Docker não encontrado no PATH." }

function Test-DockerEngineAvailable {
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & docker info --format '{{.ServerVersion}}' *> $null
        $dockerInfoExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorActionPreference }
    return $dockerInfoExitCode -eq 0
}

function Get-DockerDesktopPath {
    if ($env:OS -ne 'Windows_NT') { return $null }

    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates.Add((Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'))
    }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidates.Add((Join-Path $env:LOCALAPPDATA 'Programs\Docker\Docker\Docker Desktop.exe'))
    }
    if (-not [string]::IsNullOrWhiteSpace($env:JORNADA_DOCKER_DESKTOP_PATH)) {
        $candidates.Insert(0, $env:JORNADA_DOCKER_DESKTOP_PATH)
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    return $null
}

function Ensure-DockerEngine {
    if (Test-DockerEngineAvailable) { return }

    if ($env:OS -ne 'Windows_NT') {
        throw 'Docker Engine não está em execução.'
    }

    $dockerDesktopPath = Get-DockerDesktopPath
    if ([string]::IsNullOrWhiteSpace($dockerDesktopPath)) {
        throw 'Docker Engine não está em execução e o Docker Desktop não foi encontrado. Inicie-o manualmente ou defina JORNADA_DOCKER_DESKTOP_PATH.'
    }

    $desktopProcess = Get-Process -Name 'Docker Desktop' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $desktopProcess) {
        Write-Host "Docker Engine parado; iniciando Docker Desktop: $dockerDesktopPath"
        Start-Process -FilePath $dockerDesktopPath | Out-Null
    }
    else {
        Write-Host 'Docker Desktop já está iniciando; aguardando o Engine ficar disponível...'
    }

    for ($attempt = 1; $attempt -le 90; $attempt++) {
        if (Test-DockerEngineAvailable) {
            $serverVersion = (& docker info --format '{{.ServerVersion}}').Trim()
            Write-Host "Docker Engine pronto: $serverVersion"
            return
        }
        if ($attempt -eq 1 -or $attempt % 10 -eq 0) {
            Write-Host "Aguardando Docker Engine... tentativa $attempt/90"
        }
        Start-Sleep -Seconds 2
    }

    throw 'Docker Desktop foi iniciado, mas o Docker Engine não ficou disponível em 180 segundos.'
}

Ensure-DockerEngine
if (-not (Test-Path $EnvFile)) {
    Copy-Item $Example $EnvFile
    Write-Warning 'Criado .env local a partir de .env.example. Revise a senha antes de uso compartilhado.'
}

$vars = @{}
Get-Content $EnvFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
        $parts = $line.Split('=',2)
        $vars[$parts[0].Trim()] = $parts[1]
    }
}
$password = $vars['JORNADA_SQL_SA_PASSWORD']
$port = if ($vars['JORNADA_SQL_PORT']) { $vars['JORNADA_SQL_PORT'] } else { '14333' }
$db = if ($vars['JORNADA_SQL_DATABASE']) { $vars['JORNADA_SQL_DATABASE'] } else { 'JornadaLocal' }
if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD não definido.' }
if ($db -notmatch '^[A-Za-z0-9_]+$') { throw 'JORNADA_SQL_DATABASE inválido.' }

function Invoke-Compose {
    param([Parameter(Mandatory=$true)][string[]]$ComposeArgs)
    Push-Location $Root
    try {
        & docker compose --env-file $EnvFile @ComposeArgs
        if ($LASTEXITCODE -ne 0) { throw "docker compose falhou ($LASTEXITCODE)." }
    }
    finally { Pop-Location }
}
function Invoke-SqlCmd {
    param([Parameter(Mandatory=$true)][string[]]$SqlCmdArgs)
    Push-Location $Root
    try {
        # O baseline v3.70 usa diretivas :r relativas ao diretório /workspace.
        & docker compose --env-file $EnvFile exec -T -w /workspace -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I @SqlCmdArgs
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd falhou ($LASTEXITCODE)." }
    } finally { Pop-Location }
}
function Invoke-SqlScalar {
    param([Parameter(Mandatory=$true)][string]$Query)
    Push-Location $Root
    try {
        $raw = @(& docker compose --env-file $EnvFile exec -T -w /workspace -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -W -h -1 -Q "SET NOCOUNT ON; $Query")
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd scalar falhou ($LASTEXITCODE)." }
        $value = @($raw | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ } | Select-Object -Last 1)
        if ($value.Count -eq 0) { throw 'Consulta scalar não retornou valor.' }
        return [string]$value[0]
    }
    finally { Pop-Location }
}
function Wait-Healthy {
    Push-Location $Root
    try {
        for ($i=0; $i -lt 60; $i++) {
            $rawState = @(& docker compose --env-file $EnvFile ps --format json sqlserver)
            if ($LASTEXITCODE -ne 0) { throw "docker compose ps falhou ($LASTEXITCODE)." }
            $jsonText = ($rawState -join "`n").Trim()
            if ([string]::IsNullOrWhiteSpace($jsonText)) { throw 'Container SQL Server não foi criado pelo docker compose.' }
            try { $rows = @($jsonText | ConvertFrom-Json) }
            catch { throw "Saída JSON inválida de docker compose ps: $jsonText" }
            $row = @($rows | Where-Object { $_.Service -eq 'sqlserver' -or $_.Name -eq 'jornada-sqlserver-local' } | Select-Object -First 1)
            if ($row.Count -eq 0) { throw 'Container SQL Server não foi criado pelo docker compose.' }
            $containerState = [string]$row[0].State
            $health = [string]$row[0].Health
            if ([string]::IsNullOrWhiteSpace($health) -and $row[0].Status -match '\((healthy|unhealthy|starting)\)') { $health = $Matches[1] }
            if ($health -eq 'healthy') { return }
            if ($containerState -match 'exited|dead') {
                & docker compose --env-file $EnvFile logs --tail 80 sqlserver
                throw "SQL Server encerrou antes de ficar healthy (state=$containerState)."
            }
            if ($health -eq 'unhealthy') {
                & docker compose --env-file $EnvFile logs --tail 80 sqlserver
                throw 'SQL Server ficou unhealthy durante a inicialização.'
            }
            Start-Sleep -Seconds 2
        }
        & docker compose --env-file $EnvFile logs --tail 80 sqlserver
        throw 'SQL Server não ficou healthy no tempo esperado.'
    }
    finally { Pop-Location }
}
function Ensure-ProgressiveIdentityBackfill {
    $hasSilver = Invoke-SqlScalar -Query "SELECT CASE WHEN OBJECT_ID('silver.pessoa_origem','U') IS NULL THEN 0 ELSE 1 END;"
    if ($hasSilver -ne '1') { return }

    # Em base local existente, instala/reaplica somente a persistência progressiva antes
    # do cutover fail-closed. A lógica de criação do initial_uuid permanece na procedure canônica.
    Invoke-SqlCmd -SqlCmdArgs @('-d', $db, '-i', 'database/Jornada_Identidade_Progressiva.sql')
    $remaining = [long](Invoke-SqlScalar -Query "SELECT COUNT_BIG(*) FROM silver.pessoa_origem o LEFT JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id WHERE p.pessoa_origem_id IS NULL;")
    while ($remaining -gt 0) {
        Write-Host "Backfill progressivo local: $remaining origens sem initial_uuid..."
        Invoke-SqlCmd -SqlCmdArgs @('-d', $db, '-v', 'PAGE_SIZE=1000', '-i', 'scripts/local-progressive-identity-backfill.sql')
        $next = [long](Invoke-SqlScalar -Query "SELECT COUNT_BIG(*) FROM silver.pessoa_origem o LEFT JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id WHERE p.pessoa_origem_id IS NULL;")
        if ($next -ge $remaining) { throw "Backfill progressivo local não avançou: restantes=$next." }
        $remaining = $next
    }
    Write-Host 'Backfill progressivo local concluído: todas as origens possuem initial_uuid.'
}
function Ensure-SyntheticScale {
    $count = Invoke-SqlScalar -Query "SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-%';"
    if ($count -eq '0') {
        Write-Host 'Carregando corpus sintético local para calibração/linkage...'
        Invoke-SqlCmd -SqlCmdArgs @('-d', $db, '-v', 'SCALE_PEOPLE=5000', 'SCALE_PAIRED=5000', 'SCALE_PENDING=1000', 'SCALE_SEED=355', 'SCALE_COLLISION_MODULO=37', 'SCALE_BIRTH_SHIFT_MODULO=29', '-i', 'database/Jornada_Dev_SyntheticScale.sql')
        $count = Invoke-SqlScalar -Query "SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-%';"
    }
    if ($count -ne '11000') {
        throw "Massa sintética local inconsistente: esperadas 11000 observações de origem SCALE; encontradas=$count. Execute .\scripts\local-db.ps1 reset."
    }
    Write-Host 'Corpus sintético local pronto: 5000 pessoas Gold, 5000 pares corroborados e 1000 pendentes.'
}
function Bootstrap {
    Invoke-SqlCmd -SqlCmdArgs @('-Q', "IF DB_ID(N'$db') IS NULL CREATE DATABASE [$db];")

    # Upgrade local de volume existente: o baseline v3.70 contém um cutover que deve
    # continuar falhando fechado. Antes de reaplicá-lo, concluímos o backfill paginado
    # exigido pela própria migração, sem apagar a base e sem reduzir a proteção normativa.
    Ensure-ProgressiveIdentityBackfill

    # Ponto único de instalação nova do SQL Server normativo. O consolidado v3.70
    # aplica todas as extensões operacionais e só promove o marcador após provar completude.
    Invoke-SqlCmd -SqlCmdArgs @('-d', $db, '-i', 'database/Jornada_Fase1_v3.70.sql')
    Invoke-SqlCmd -SqlCmdArgs @('-d', $db, '-i', 'database/Jornada_Seed_Dev.sql')
    Invoke-SqlCmd -SqlCmdArgs @('-d', $db, '-i', 'database/Jornada_Seed_Dev_UniquePayloads.sql')
    # Reaplicação idempotente necessária em DEV para reservar CPFs históricos do seed.
    Invoke-SqlCmd -SqlCmdArgs @('-d', $db, '-i', 'database/migrations/20260907_Cpf_Ancora.sql')
    Invoke-SqlCmd -SqlCmdArgs @('-d', $db, '-i', 'database/migrations/20260910_Schema_Consolidation_370.sql')

    # O banco local canônico carrega o corpus de 5k por padrão. Harnesses que controlam
    # sua própria massa (por exemplo, escala) usam -NoSyntheticCorpus e carregam o corpus
    # explicitamente depois do reset, sem apagar ou duplicar dados SCALE.
    if (-not $NoSyntheticCorpus) {
        Ensure-SyntheticScale
    }

    # Seed e eventual massa SCALE são inserções DEV diretas e não passam pelo Processor.
    Ensure-ProgressiveIdentityBackfill
}

switch ($Action) {
    'up' {
        Invoke-Compose -ComposeArgs @('up','-d','sqlserver'); Wait-Healthy; Bootstrap
        Write-Host "SQL Server Developer local pronto: localhost:$port / $db (schema 3.70)"
    }
    'reset' {
        Invoke-Compose -ComposeArgs @('up','-d','sqlserver'); Wait-Healthy
        Invoke-SqlCmd -SqlCmdArgs @('-Q', "IF DB_ID(N'$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END; CREATE DATABASE [$db];")
        Bootstrap
        if ($NoSyntheticCorpus) {
            Write-Host "Banco local recriado sem corpus SCALE: $db (schema 3.70)"
        }
        else {
            Write-Host "Banco local recriado: $db (schema 3.70)"
        }
    }
    'backfill' {
        Invoke-Compose -ComposeArgs @('up','-d','sqlserver'); Wait-Healthy; Ensure-ProgressiveIdentityBackfill
    }
    'down' { Invoke-Compose -ComposeArgs @('down') }
    'clean' { Invoke-Compose -ComposeArgs @('down','-v') }
    'status' { Invoke-Compose -ComposeArgs @('ps') }
}