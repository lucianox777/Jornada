param(
    [ValidateSet('up','reset','down','clean','status','logs','calibrate','linkage')]
    [string]$Action = 'up',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
$Example = Join-Path $Root '.env.example'
$LocalDb = Join-Path $PSScriptRoot 'local-db.ps1'
$ClusterConfig = Join-Path $Root 'install\windows-production\Jornada.Cluster.Test.json'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker não encontrado no PATH.' }
if (-not (Test-Path -LiteralPath $EnvFile)) {
    Copy-Item -LiteralPath $Example -Destination $EnvFile
    Write-Host 'Criado .env local com as credenciais sintéticas padrão de teste.'
}

function Get-EnvValue([string]$Name) {
    foreach ($line in Get-Content -LiteralPath $EnvFile) {
        if ($line -match '^\s*#' -or [string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split '=', 2
        if ($parts.Count -eq 2 -and $parts[0].Trim() -eq $Name) { return $parts[1].Trim() }
    }
    return $null
}

function Invoke-Compose {
    param([Parameter(Mandatory=$true)][string[]]$ComposeArgs)
    Push-Location $Root
    try {
        & docker compose --env-file $EnvFile @ComposeArgs
        if ($LASTEXITCODE -ne 0) { throw "docker compose falhou ($LASTEXITCODE)." }
    }
    finally { Pop-Location }
}

function Get-SqlScalar([string]$Query) {
    $password = Get-EnvValue 'JORNADA_SQL_SA_PASSWORD'
    if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD ausente do .env.' }
    Push-Location $Root
    try {
        $lines = @(& docker compose --env-file $EnvFile exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd `
            -S localhost -U sa -P $password -C -d JornadaLocal -W -h -1 -Q "SET NOCOUNT ON; $Query")
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd falhou ($LASTEXITCODE)." }
        $value = @($lines | ForEach-Object { $_.Trim() } | Where-Object { $_ }) | Select-Object -Last 1
        if ($null -eq $value) { return '' }
        return [string]$value
    }
    finally { Pop-Location }
}

function Invoke-Node2 {
    param([Parameter(Mandatory=$true)][string[]]$Command)
    Invoke-Compose -ComposeArgs (@('exec','-T','jornada-node2') + $Command)
}

function Ensure-LocalBlockingProjection {
    Write-Host 'Verificando projeção de blocking da massa sintética local...'
    Invoke-Node2 -Command @('env','Processor__Operation=REBUILD_LOCAL_BLOCKING','dotnet','/opt/jornada/apps/Jornada.Processor.Worker/Jornada.Processor.Worker.dll')
}

function Wait-NodeReady([string]$Name, [string]$Url) {
    for ($i = 0; $i -lt 120; $i++) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 2
            if ([int]$response.StatusCode -eq 200) { Write-Host "$Name ready: $Url"; return }
        }
        catch { }
        Start-Sleep -Seconds 1
    }
    Invoke-Compose -ComposeArgs @('logs','--tail','120',$Name)
    throw "$Name não ficou ready: $Url"
}

function Show-Endpoints {
    $config = Get-Content -Raw -Encoding UTF8 $ClusterConfig | ConvertFrom-Json
    Write-Host ''
    Write-Host 'Cluster local pronto (4 containers canônicos):'
    Write-Host '  NODE1: http://127.0.0.1:5080'
    Write-Host '  NODE2: http://127.0.0.1:5180'
    Write-Host '  SQL:   localhost:14333'
    Write-Host '  NAS:   jornada-nas:445 / share bronze (host: localhost:1445)'
    Write-Host "  Config bundle: $($config.configurationBundleVersion) / SolutionSchema $($config.solutionSchema)"
    Write-Host ''
    Write-Host 'Monitor operacional:'
    Write-Host '  NODE1: http://127.0.0.1:5080/monitor'
    Write-Host '  NODE2: http://127.0.0.1:5180/monitor'
    Write-Host '  Com VIP/LB externo, use /monitor no endereço do balanceador.'
    Write-Host ''
    Write-Host 'Execuções únicas recomendadas no NODE2 (não ficam em background e não são agendadas):'
    Write-Host '  Calibrador completo: .\scripts\local-cluster.ps1 calibrate'
    Write-Host '    /opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll'
    Write-Host '  Linkage:             .\scripts\local-cluster.ps1 linkage'
    Write-Host '    /opt/jornada/apps/Jornada.Linkage.Runner/Jornada.Linkage.Runner.dll'
    Write-Host '  Bronze Verify:'
    Write-Host '    docker compose --env-file .env exec -T jornada-node2 dotnet /opt/jornada/tools/Jornada.Bronze.Verify/Jornada.Bronze.Verify.dll --help'
    Write-Host '  Linkage Evaluation (DEV/HML only):'
    Write-Host '    docker compose --env-file .env exec -T jornada-node2 dotnet /opt/jornada/tools/Jornada.Linkage.Evaluation/Jornada.Linkage.Evaluation.dll --help'
    Write-Host '  Integrador C#:'
    Write-Host '    docker compose --env-file .env exec -T jornada-node2 dotnet /opt/jornada/clients/Jornada.Integrador/Jornada.Integrador.CSharp.dll --help'
}

function Start-Nodes([switch]$Build) {
    $args = @('up','-d')
    if ($Build) { $args += '--build' }
    $args += @('jornada-node1','jornada-node2')
    Invoke-Compose -ComposeArgs $args
    Wait-NodeReady 'jornada-node1' 'http://127.0.0.1:5080/health/ready'
    Wait-NodeReady 'jornada-node2' 'http://127.0.0.1:5180/health/ready'
    Ensure-LocalBlockingProjection
    Show-Endpoints
}

function Invoke-Calibration {
    Ensure-LocalBlockingProjection
    $beforeText = Get-SqlScalar "SELECT ISNULL(MAX(versao),0) FROM identidade.modelo_linkage;"
    $before = [int]$beforeText
    Write-Host "Calibração iniciando após modelo v$before."
    Invoke-Node2 -Command @('env','LinkageParameters__Operation=GENERATE_DRAFT','LinkageParameters__RunOnce=true','dotnet','/opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll')
    $count = [int](Get-SqlScalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE versao>$before AND status='RASCUNHO';")
    if ($count -ne 1) { throw "Esperado exatamente um novo RASCUNHO; encontrados=$count." }
    $version = [int](Get-SqlScalar "SELECT MAX(versao) FROM identidade.modelo_linkage WHERE versao>$before AND status='RASCUNHO';")
    Invoke-Node2 -Command @('env','LinkageParameters__Operation=VALIDATE',"LinkageParameters__TargetVersion=$version",'LinkageParameters__RunOnce=true','dotnet','/opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll')
    Invoke-Node2 -Command @('env','LinkageParameters__Operation=ACTIVATE',"LinkageParameters__TargetVersion=$version",'LinkageParameters__RunOnce=true','dotnet','/opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll')
    $active = [int](Get-SqlScalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE versao=$version AND status='ATIVO' AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO';")
    if ($active -ne 1) { throw "Modelo v$version não ficou ATIVO como modelo calibrado." }
    Write-Host "Calibração concluída: modelo calibrado v$version ATIVO."
}

function Invoke-Linkage {
    Ensure-LocalBlockingProjection
    $active = [int](Get-SqlScalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO';")
    if ($active -ne 1) {
        throw "Linkage bloqueado: encontrados $active modelos calibrados ATIVOS. O seed sintético não libera execução. Execute primeiro '.\scripts\local-cluster.ps1 calibrate'."
    }
    $version = Get-SqlScalar "SELECT TOP(1) versao FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO' ORDER BY versao DESC;"
    Write-Host "Executando linkage com modelo calibrado ATIVO v$version."
    Invoke-Node2 -Command @('dotnet','/opt/jornada/apps/Jornada.Linkage.Runner/Jornada.Linkage.Runner.dll','--mode','ON_DEMAND','--publish','true','--requested-by','LOCAL_CLUSTER','--reason','manual-local-cluster')
}

switch ($Action) {
    'up' { & $LocalDb up; if ($LASTEXITCODE -ne 0) { throw "local-db.ps1 up falhou ($LASTEXITCODE)." }; Start-Nodes -Build:(-not $NoBuild) }
    'reset' { Invoke-Compose -ComposeArgs @('stop','jornada-node1','jornada-node2'); & $LocalDb reset; if ($LASTEXITCODE -ne 0) { throw "local-db.ps1 reset falhou ($LASTEXITCODE)." }; Start-Nodes }
    'down' { Invoke-Compose -ComposeArgs @('down') }
    'clean' { Invoke-Compose -ComposeArgs @('down','-v','--remove-orphans') }
    'status' { Invoke-Compose -ComposeArgs @('ps') }
    'logs' { Invoke-Compose -ComposeArgs @('logs','-f','jornada-node1','jornada-node2','jornada-nas') }
    'calibrate' { Invoke-Calibration }
    'linkage' { Invoke-Linkage }
}
