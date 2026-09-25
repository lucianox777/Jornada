[CmdletBinding()]
param(
    [switch]$OpenMonitor
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = $PSScriptRoot
$Solution = Join-Path $Root 'Jornada.sln'
$ClusterScript = Join-Path $Root 'scripts\local-cluster.ps1'
$EnvFile = Join-Path $Root '.env'
$EnvExample = Join-Path $Root '.env.example'
$TotalSteps = 9
$script:CurrentStep = 0

function Write-Banner {
    Write-Host ''
    Write-Host '============================================================'
    Write-Host ' JORNADA DO CIDADÃO - INSTALAÇÃO LOCAL DOCKER'
    Write-Host '============================================================'
    Write-Host ''
}

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)] [string]$Description,
        [Parameter(Mandatory = $true)] [scriptblock]$Action
    )

    $script:CurrentStep++
    $prefix = '[{0}/{1}] {2}' -f $script:CurrentStep, $TotalSteps, $Description
    Write-Host $prefix
    try {
        & $Action
        Write-Host ('      OK: {0}' -f $Description)
    }
    catch {
        Write-Host ('      FALHOU: {0}' -f $Description) -ForegroundColor Red
        Write-Host ''
        Write-Host ('ERRO: {0}' -f $_.Exception.Message) -ForegroundColor Red
        Write-Host ''
        Write-Host 'Diagnóstico do cluster: .\scripts\local-cluster.ps1 logs'
        throw
    }
}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)] [string]$FilePath,
        [Parameter(Mandatory = $true)] [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath falhou com ExitCode=$LASTEXITCODE."
    }
}

function Assert-Command([string]$Name, [string]$FriendlyName) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$FriendlyName não encontrado no PATH."
    }
}

function Assert-DockerEngine {
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & docker info --format '{{.ServerVersion}}' *> $null
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($exitCode -ne 0) {
        throw 'Docker Engine não está em execução. Inicie o Docker Desktop e tente novamente.'
    }
}

function Invoke-Compose([string[]]$Arguments) {
    Push-Location $Root
    try {
        Invoke-Native 'docker' (@('compose','--env-file',$EnvFile) + $Arguments)
    }
    finally {
        Pop-Location
    }
}

function Assert-Http200([string]$Url, [string]$Name) {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 10
        if ([int]$response.StatusCode -ne 200) {
            throw "$Name respondeu HTTP $($response.StatusCode)."
        }
    }
    catch {
        throw "$Name não respondeu corretamente em $Url. $($_.Exception.Message)"
    }
}

Write-Banner

Invoke-Step 'Verificando Docker' {
    Assert-Command 'docker' 'Docker'
    Assert-DockerEngine
    Invoke-Native 'docker' @('compose','version')
}

Invoke-Step 'Verificando .NET SDK' {
    Assert-Command 'dotnet' '.NET SDK'
    Invoke-Native 'dotnet' @('--version')
    if (-not (Test-Path -LiteralPath $Solution)) { throw "Solution não encontrada: $Solution" }
}

Invoke-Step 'Preparando configuração local' {
    if (-not (Test-Path -LiteralPath $EnvFile)) {
        if (-not (Test-Path -LiteralPath $EnvExample)) { throw ".env.example não encontrado: $EnvExample" }
        Assert-Command 'python' 'Python 3'
        Invoke-Native 'python' @((Join-Path $Root 'scripts/local_env_bootstrap.py'),'--check-docker-volume')
    }
    Invoke-Compose @('config','--quiet')
}

Invoke-Step 'Restaurando dependências NuGet em modo bloqueado' {
    Push-Location $Root
    try { Invoke-Native 'dotnet' @('restore','Jornada.sln','--locked-mode') }
    finally { Pop-Location }
}

Invoke-Step 'Compilando Jornada em Release' {
    Push-Location $Root
    try { Invoke-Native 'dotnet' @('build','Jornada.sln','--configuration','Release','--no-restore','-warnaserror') }
    finally { Pop-Location }
}

Invoke-Step 'Construindo imagens Docker da Jornada e do NAS' {
    Invoke-Compose @('build','jornada-node1','jornada-nas')
    Write-Host '      jornada-node:test pronta.'
    Write-Host '      jornada-nas:test pronta.'
}

Invoke-Step 'Inicializando SQL Server e banco Jornada' {
    & $ClusterScript up -NoBuild
    if ($LASTEXITCODE -ne 0) { throw "local-cluster.ps1 up falhou com ExitCode=$LASTEXITCODE." }
}

Invoke-Step 'Verificando saúde de NODE1 e NODE2' {
    Assert-Http200 'http://127.0.0.1:5080/health/ready' 'NODE1'
    Assert-Http200 'http://127.0.0.1:5180/health/ready' 'NODE2'
}

Invoke-Step 'Verificando monitor operacional' {
    Assert-Http200 'http://127.0.0.1:5080/monitor' 'Monitor NODE1'
    Assert-Http200 'http://127.0.0.1:5180/monitor' 'Monitor NODE2'
}

Write-Host ''
Write-Host '------------------------------------------------------------'
Write-Host ' JORNADA PRONTA'
Write-Host '------------------------------------------------------------'
Write-Host 'NODE1:   http://127.0.0.1:5080'
Write-Host 'NODE2:   http://127.0.0.1:5180'
Write-Host 'Monitor: http://127.0.0.1:5080/monitor'
Write-Host 'SQL:     localhost:14333'
Write-Host 'NAS:     localhost:1445'
Write-Host ''
Write-Host 'Operações one-shot governadas:'
Write-Host '  Calibrador: .\scripts\local-cluster.ps1 calibrate'
Write-Host '  Linkage:    .\scripts\local-cluster.ps1 linkage'
Write-Host ''
Write-Host 'Executáveis standalone no NODE2 (uma linha por comando):'
Write-Host '  Bronze Verify:       docker compose --env-file .env exec -T jornada-node2 dotnet /opt/jornada/tools/Jornada.Bronze.Verify/Jornada.Bronze.Verify.dll --help'
Write-Host '  Linkage Evaluation:  docker compose --env-file .env exec -T jornada-node2 dotnet /opt/jornada/tools/Jornada.Linkage.Evaluation/Jornada.Linkage.Evaluation.dll --help'
Write-Host '  Integrador C#:       docker compose --env-file .env exec -T jornada-node2 dotnet /opt/jornada/clients/Jornada.Integrador/Jornada.Integrador.CSharp.dll --help'
Write-Host ''
Write-Host 'Observação: no container Linux os executáveis .NET são chamados como dotnet <arquivo>.dll; no bundle Windows os mesmos projetos são publicados como .exe.'
Write-Host ''
Write-Host 'Administração local:'
Write-Host '  Ver status:      .\scripts\local-cluster.ps1 status'
Write-Host '  Ver logs:        .\scripts\local-cluster.ps1 logs'
Write-Host '  Limpar ambiente: .\Clean-JornadaLocal.ps1'
Write-Host ''

if ($OpenMonitor) {
    Start-Process 'http://127.0.0.1:5080/monitor'
}
