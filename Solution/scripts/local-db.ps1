param(
    [ValidateSet('up','reset','down','clean','status','backfill')]
    [string]$Action = 'up',
    [switch]$NoSyntheticCorpus,
    [string]$DatabaseName,
    [string]$EnvFile,
    [ValidateSet('HML','DEV','PROD')]
    [string]$RuntimeMode,
    [switch]$ConfirmProductionReset
)
$ErrorActionPreference = 'Stop'
if([string]::IsNullOrWhiteSpace($RuntimeMode)){
    $RuntimeMode=if([string]::IsNullOrWhiteSpace($env:JORNADA_RUNTIME_MODE)){'HML'}else{$env:JORNADA_RUNTIME_MODE.Trim().ToUpperInvariant()}
}
$RuntimeMode=$RuntimeMode.ToUpperInvariant()
if($RuntimeMode -notin @('HML','DEV','PROD')){throw "RuntimeMode inválido: $RuntimeMode."}
$ResidentEnvironmentProfile=switch($RuntimeMode){'DEV'{'Development'}'PROD'{'Production'}default{'Homologation'}}
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$DefaultEnvFile = Join-Path $Root '.env'
$ExplicitEnvFile = -not [string]::IsNullOrWhiteSpace($EnvFile)
$EnvFile = if ($ExplicitEnvFile) {
    [IO.Path]::GetFullPath($EnvFile)
} elseif (-not [string]::IsNullOrWhiteSpace($env:JORNADA_LOCAL_ENV_FILE)) {
    [IO.Path]::GetFullPath($env:JORNADA_LOCAL_ENV_FILE)
} else {
    $DefaultEnvFile
}
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
if (-not (Test-Path -LiteralPath $EnvFile -PathType Leaf)) {
    if ($ExplicitEnvFile) { throw "EnvFile aponta para arquivo inexistente: $EnvFile" }
    if (-not [string]::IsNullOrWhiteSpace($env:JORNADA_LOCAL_ENV_FILE)) { throw "JORNADA_LOCAL_ENV_FILE aponta para arquivo inexistente: $EnvFile" }
    if (-not (Get-Command python -ErrorAction SilentlyContinue)) { throw 'Python 3 é necessário para gerar a credencial local.' }
    & python (Join-Path $PSScriptRoot 'local_env_bootstrap.py') --check-docker-volume
    if ($LASTEXITCODE -ne 0) { throw 'Bootstrap seguro do .env falhou; banco original preservado.' }
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
$db = if (-not [string]::IsNullOrWhiteSpace($DatabaseName)) {
    $DatabaseName
}
elseif ($vars['JORNADA_SQL_DATABASE']) {
    $vars['JORNADA_SQL_DATABASE']
}
else {
    'JornadaLocal'
}
if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD não definido.' }
if ($db -notmatch '^[A-Za-z0-9_]+$') { throw 'Nome de banco local inválido.' }

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
    $previousSqlcmdPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
    Push-Location $Root
    try {
        $env:SQLCMDPASSWORD = $password
        # O baseline v3.70 usa diretivas :r relativas ao diretório /workspace.
        & docker compose --env-file $EnvFile exec -T -w /workspace -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I @SqlCmdArgs
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd falhou ($LASTEXITCODE)." }
    }
    finally {
        if ($null -eq $previousSqlcmdPassword) {
            Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
        } else {
            $env:SQLCMDPASSWORD = $previousSqlcmdPassword
        }
        Pop-Location
    }
}
function Invoke-SqlScalar {
    param([Parameter(Mandatory=$true)][string]$Query)
    $previousSqlcmdPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
    Push-Location $Root
    try {
        $env:SQLCMDPASSWORD = $password
        $raw = @(& docker compose --env-file $EnvFile exec -T -w /workspace -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -W -h -1 -Q "SET NOCOUNT ON; $Query")
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd scalar falhou ($LASTEXITCODE)." }
        $value = @($raw | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ } | Select-Object -Last 1)
        if ($value.Count -eq 0) { throw 'Consulta scalar não retornou valor.' }
        return [string]$value[0]
    }
    finally {
        if ($null -eq $previousSqlcmdPassword) {
            Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
        } else {
            $env:SQLCMDPASSWORD = $previousSqlcmdPassword
        }
        Pop-Location
    }
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
function Get-SyntheticScaleCounts {
    $line = Invoke-SqlScalar -Query @"
SELECT CONCAT(
    SUM(CASE WHEN codigo_pessoa_origem LIKE N'SCALE-SEHAB-%' THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END),'|',
    SUM(CASE WHEN codigo_pessoa_origem LIKE N'SCALE-SMADS-%' THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END),'|',
    SUM(CASE WHEN codigo_pessoa_origem LIKE N'SCALE-PEND-%' THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END),'|',
    SUM(CASE WHEN codigo_pessoa_origem LIKE N'SCALE-%'
              AND codigo_pessoa_origem NOT LIKE N'SCALE-SEHAB-%'
              AND codigo_pessoa_origem NOT LIKE N'SCALE-SMADS-%'
              AND codigo_pessoa_origem NOT LIKE N'SCALE-PEND-%'
             THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END))
FROM silver.pessoa_origem;
"@
    $parts = $line.Split('|')
    if ($parts.Count -ne 4) { throw "Contagem da massa SCALE inválida: $line" }
    return [ordered]@{
        Sehab = [long]$parts[0]
        Smads = [long]$parts[1]
        Pending = [long]$parts[2]
        ExtraFixtures = [long]$parts[3]
    }
}
function Assert-SyntheticScaleExpansion {
    param(
        [Parameter(Mandatory=$true)][long]$BasePeople,
        [Parameter(Mandatory=$true)][long]$ActualPeople
    )
    if ($ActualPeople -le $BasePeople) { return }

    $expectedExtra = $ActualPeople - $BasePeople
    $line = Invoke-SqlScalar -Query @"
DECLARE @base bigint=$BasePeople, @actual bigint=$ActualPeople;
WITH extra AS (
    SELECT po.pessoa_origem_id,
           TRY_CONVERT(bigint,RIGHT(po.codigo_pessoa_origem,10)) AS n
    FROM silver.pessoa_origem po
    WHERE po.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%'
      AND TRY_CONVERT(bigint,RIGHT(po.codigo_pessoa_origem,10))>@base
),
valid AS (
    SELECT DISTINCT e.pessoa_origem_id,e.n
    FROM extra e
    JOIN silver.pessoa_observacao o
      ON o.pessoa_origem_id=e.pessoa_origem_id
    JOIN identidade.vinculo_fonte vf
      ON vf.pessoa_observacao_id=o.pessoa_observacao_id
     AND vf.ativo=1
     AND vf.metodo_resolucao=N'CPF_DETERMINISTICO'
     AND vf.motivo=N'SCALE_INCREMENTAL'
    JOIN gold.pessoa g
      ON g.pessoa_uuid=vf.pessoa_uuid
     AND g.cpf COLLATE Latin1_General_100_BIN2=o.cpf COLLATE Latin1_General_100_BIN2
    JOIN identidade.cpf_ancora a
      ON a.pessoa_uuid=vf.pessoa_uuid
     AND a.cpf=o.cpf COLLATE Latin1_General_100_BIN2
)
SELECT CONCAT(
    (SELECT COUNT_BIG(*) FROM extra),'|',
    COALESCE((SELECT MIN(n) FROM extra),0),'|',
    COALESCE((SELECT MAX(n) FROM extra),0),'|',
    (SELECT COUNT_BIG(DISTINCT n) FROM extra),'|',
    (SELECT COUNT_BIG(*) FROM valid)
);
"@
    $parts=$line.Split('|')
    if($parts.Count -ne 5){throw "Validação da expansão SCALE inválida: $line"}
    $extraCount=[long]$parts[0]
    $minN=[long]$parts[1]
    $maxN=[long]$parts[2]
    $distinctN=[long]$parts[3]
    $validCount=[long]$parts[4]
    if($extraCount -ne $expectedExtra -or $distinctN -ne $expectedExtra -or
       $minN -ne ($BasePeople+1) -or $maxN -ne $ActualPeople -or $validCount -ne $expectedExtra){
        throw "Expansão SCALE-SEHAB inconsistente: base=$BasePeople atual=$ActualPeople extras=$extraCount distintos=$distinctN faixa=$minN..$maxN válidos=$validCount. Somente expansões geradas pela Console DEV podem ser preservadas."
    }
    Write-Host "Expansão SCALE-SEHAB controlada preservada: base=$BasePeople; atual=$ActualPeople; adicionais=$expectedExtra." -ForegroundColor Green
}
function Ensure-SyntheticScale {
    $expectedPeople = if ($vars['JORNADA_LOCAL_SYNTHETIC_PEOPLE']) { [long]$vars['JORNADA_LOCAL_SYNTHETIC_PEOPLE'] } else { 5000 }
    $expectedPaired = if ($vars['JORNADA_LOCAL_SYNTHETIC_PAIRED']) { [long]$vars['JORNADA_LOCAL_SYNTHETIC_PAIRED'] } else { 5000 }
    $expectedPending = if ($vars['JORNADA_LOCAL_SYNTHETIC_PENDING']) { [long]$vars['JORNADA_LOCAL_SYNTHETIC_PENDING'] } else { 1000 }
    if($expectedPeople -lt 1000 -or $expectedPeople -gt 5000000){throw 'JORNADA_LOCAL_SYNTHETIC_PEOPLE fora do intervalo suportado.'}
    if($expectedPaired -lt 2 -or $expectedPaired -gt $expectedPeople){throw 'JORNADA_LOCAL_SYNTHETIC_PAIRED inválido.'}
    if($expectedPending -lt 0 -or $expectedPending -gt 2000000){throw 'JORNADA_LOCAL_SYNTHETIC_PENDING inválido.'}

    $counts = Get-SyntheticScaleCounts
    $canonicalTotal = [long]$counts['Sehab'] + [long]$counts['Smads'] + [long]$counts['Pending']
    if ($canonicalTotal -eq 0) {
        if ([long]$counts['ExtraFixtures'] -gt 0) {
            throw "Fixtures SCALE adicionais existem sem a massa canônica (extras=$($counts['ExtraFixtures'])). Revise o ambiente sintético da Console DEV antes de continuar."
        }
        $initialPending=if($RuntimeMode -eq 'DEV'){0}else{$expectedPending}
        Write-Host "Carregando corpus sintético base para calibração/linkage: Gold=$expectedPeople, pares=$expectedPaired, pendentes_iniciais=$initialPending..."
        Invoke-SqlCmd -SqlCmdArgs @('-d', $db, '-v', "SCALE_PEOPLE=$expectedPeople", "SCALE_PAIRED=$expectedPaired", "SCALE_PENDING=$initialPending", 'SCALE_SEED=355', 'SCALE_COLLISION_MODULO=37', 'SCALE_BIRTH_SHIFT_MODULO=29', '-i', 'database/Jornada_Dev_SyntheticScale.sql')
        $counts = Get-SyntheticScaleCounts
    }
    $actualPeople=[long]$counts['Sehab']
    $actualPaired=[long]$counts['Smads']
    $actualPending=[long]$counts['Pending']

    # Trocar HML -> DEV no mesmo banco é aditivo: somente o opt-in DEV materializa
    # os 6.000 SCALE-PEND. Não há reset implícito nem troca de banco.
    if($actualPeople -ge $expectedPeople -and $actualPaired -eq $expectedPaired -and $actualPending -lt $expectedPending){
        if($RuntimeMode -ne 'DEV'){
            throw "Somente DEV pode expandir SCALE-PEND; modo atual=$RuntimeMode."
        }
        Write-Host "DEV: expandindo corpus adicional SCALE-PEND de $actualPending para $expectedPending sem reset..."
        Invoke-SqlCmd -SqlCmdArgs @('-d',$db,'-v',"SCALE_PENDING=$expectedPending",'-i','database/Jornada_Dev_SyntheticPending.sql')
        $counts=Get-SyntheticScaleCounts
        $actualPeople=[long]$counts['Sehab']
        $actualPaired=[long]$counts['Smads']
        $actualPending=[long]$counts['Pending']
    }

    if($actualPending -gt $expectedPending){
        throw "O banco contém SCALE-PEND=$actualPending, mas o modo $RuntimeMode espera $expectedPending. Para reduzir a massa, use Reset explícito; o startup nunca apaga dados implicitamente."
    }
    if ($actualPeople -lt $expectedPeople -or $actualPaired -ne $expectedPaired -or $actualPending -ne $expectedPending) {
        throw "Massa sintética local inconsistente: mínimo SCALE-SEHAB=$expectedPeople, SCALE-SMADS=$expectedPaired, SCALE-PEND=$expectedPending; encontrado SEHAB=$actualPeople SMADS=$actualPaired PEND=$actualPending extras=$($counts['ExtraFixtures']). Revise o ambiente configurado antes de continuar."
    }
    Assert-SyntheticScaleExpansion -BasePeople $expectedPeople -ActualPeople $actualPeople
    if ([long]$counts['ExtraFixtures'] -gt 0) {
        Write-Host "Fixtures SCALE adicionais preservados fora da massa canônica: $($counts['ExtraFixtures'])."
    }
    Write-Host "Corpus sintético local pronto: $actualPeople pessoas Gold, $expectedPaired pares corroborados e $expectedPending pendentes."
}
function Bootstrap {
    # Operações de criação/estado do próprio banco devem partir explicitamente de master.
    # Isso evita que o login fique preso ao banco-alvo durante reset/bootstrap.
    Invoke-SqlCmd -SqlCmdArgs @('-d', 'master', '-Q', "IF DB_ID(N'$db') IS NULL CREATE DATABASE [$db];")

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
    Invoke-SqlCmd -SqlCmdArgs @('-d', $db, '-i', 'database/migrations/20260922_Processor_Lease_Heartbeat_Isolation.sql')

    # O perfil residente descreve a configuração efetiva. A semântica do pipeline
    # não depende do nome do banco: HML é o padrão, DEV é opt-in e PROD é explícito.
    Invoke-SqlCmd -SqlCmdArgs @('-d', $db, '-Q', "IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.EnvironmentProfile') EXEC sys.sp_updateextendedproperty @name=N'Jornada.EnvironmentProfile',@value=N'$ResidentEnvironmentProfile'; ELSE EXEC sys.sp_addextendedproperty @name=N'Jornada.EnvironmentProfile',@value=N'$ResidentEnvironmentProfile';")

    # O banco local canônico carrega a massa sintética configurada; o padrão histórico continua 5k. Harnesses que controlam
    # sua própria massa (por exemplo, escala) usam -NoSyntheticCorpus e carregam o corpus
    # explicitamente depois do reset, sem apagar ou duplicar dados SCALE.
    if (-not $NoSyntheticCorpus) {
        Ensure-SyntheticScale
    }

    # Seed e eventual massa SCALE são inserções DEV diretas e não passam pelo Processor.
    Ensure-ProgressiveIdentityBackfill
}

if($RuntimeMode -eq 'PROD' -and $Action -in @('reset','clean') -and -not $ConfirmProductionReset){
    throw "Operação destrutiva '$Action' bloqueada em PROD. Execute novamente com -ConfirmProductionReset no comando explícito."
}

switch ($Action) {
    'up' {
        Invoke-Compose -ComposeArgs @('up','-d','sqlserver'); Wait-Healthy; Bootstrap
        Write-Host "SQL Server Developer local pronto: localhost:$port / $db (schema 3.70)"
    }
    'reset' {
        Invoke-Compose -ComposeArgs @('up','-d','sqlserver'); Wait-Healthy
        # O reset precisa executar a partir de master. Sem isso, se a sessão administrativa
        # estiver conectada ao próprio banco-alvo, ela impede o SINGLE_USER/DROP que tenta executar.
        Invoke-SqlCmd -SqlCmdArgs @('-d', 'master', '-Q', "IF DB_ID(N'$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END; CREATE DATABASE [$db];")
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