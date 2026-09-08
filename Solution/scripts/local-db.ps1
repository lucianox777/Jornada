param(
    [ValidateSet('up','reset','down','clean','status')]
    [string]$Action = 'up'
)
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
$Example = Join-Path $Root '.env.example'
New-Item -ItemType Directory -Force (Join-Path $Root '.local/sql-backup') | Out-Null

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw "Docker não encontrado no PATH." }
function Test-DockerEngine {
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & docker info --format '{{.ServerVersion}}' *> $null
        $dockerInfoExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorActionPreference }
    if ($dockerInfoExitCode -ne 0) {
        throw 'Docker Engine não está em execução. Inicie o Docker Desktop e aguarde o Engine ficar disponível.'
    }
}
Test-DockerEngine
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
        & docker compose --env-file $EnvFile exec -T -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I @SqlCmdArgs
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd falhou ($LASTEXITCODE)." }
    } finally { Pop-Location }
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
function Bootstrap {
    Invoke-SqlCmd -SqlCmdArgs @('-Q', "IF DB_ID(N'$db') IS NULL CREATE DATABASE [$db];")
    Invoke-SqlCmd -SqlCmdArgs @('-d', $db, '-i', '/workspace/database/Jornada_Fase1.sql')
    Invoke-SqlCmd -SqlCmdArgs @('-d', $db, '-i', '/workspace/database/Jornada_Seed_Dev.sql')
    # V1 operacional: depois da massa inicial, reserva também todo CPF histórico do seed.
    # Em produção, onde não há seed DEV, a mesma migração é aplicada logo após o baseline.
    Invoke-SqlCmd -SqlCmdArgs @('-d', $db, '-i', '/workspace/database/migrations/20260907_Cpf_Ancora.sql')
}

switch ($Action) {
    'up' {
        Invoke-Compose -ComposeArgs @('up','-d','sqlserver'); Wait-Healthy; Bootstrap
        Write-Host "SQL Server Developer local pronto: localhost:$port / $db"
    }
    'reset' {
        Invoke-Compose -ComposeArgs @('up','-d','sqlserver'); Wait-Healthy
        Invoke-SqlCmd -SqlCmdArgs @('-Q', "IF DB_ID(N'$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END; CREATE DATABASE [$db];")
        Bootstrap; Write-Host "Banco local recriado: $db"
    }
    'down' { Invoke-Compose -ComposeArgs @('down') }
    'clean' { Invoke-Compose -ComposeArgs @('down','-v') }
    'status' { Invoke-Compose -ComposeArgs @('ps') }
}
