param(
    [ValidateRange(10000, 5000000)]
    [int]$PairCount = 1000000,

    [int]$Seed = 20260917
)

$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Cluster = Join-Path $PSScriptRoot 'local-cluster.ps1'
$IbgeReport = Join-Path $PSScriptRoot 'local-ibge-u-bootstrap.ps1'
$Validation = Join-Path $PSScriptRoot 'local-linkage-validation.ps1'
$OutDir = Join-Path $Root '.local\linkage-monte-carlo-validation'
$TranscriptPath = Join-Path $OutDir ('run-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.txt')

function Write-Step {
    param(
        [Parameter(Mandatory=$true)][int]$Number,
        [Parameter(Mandatory=$true)][int]$Total,
        [Parameter(Mandatory=$true)][string]$Title,
        [Parameter(Mandatory=$true)][string]$CommandText
    )

    Write-Host ''
    Write-Host "=== $Number/$Total - $Title ===" -ForegroundColor Cyan
    Write-Host "# $CommandText" -ForegroundColor DarkGray
}

function Invoke-Native {
    param(
        [Parameter(Mandatory=$true)][string]$Executable,
        [Parameter(Mandatory=$true)][string[]]$Arguments,
        [Parameter(Mandatory=$true)][string]$Label
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Label falhou com exit code $LASTEXITCODE."
    }
}

function Invoke-LocalScript {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][hashtable]$Parameters,
        [Parameter(Mandatory=$true)][string]$Label
    )

    & $Path @Parameters
    if (-not $?) {
        throw "$Label falhou."
    }
}

$previousPairCount = $env:JORNADA_LINKAGE_IBGE_MC_PAIR_COUNT
$previousSeed = $env:JORNADA_LINKAGE_IBGE_MC_SEED
$transcriptStarted = $false

try {
    Set-Location -LiteralPath $Root

    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    Start-Transcript -Path $TranscriptPath -Force | Out-Null
    $transcriptStarted = $true

    Write-Host "# Set-Location '$Root'" -ForegroundColor DarkGray
    Write-Host ''
    Write-Host 'VALIDAÇÃO DEV LINKAGE — MONTE CARLO IBGE' -ForegroundColor Yellow
    Write-Host "NOME: prenomes TODOS + sobrenomes TODOS; pares=$PairCount; seed=$Seed." -ForegroundColor Yellow
    Write-Host "NOME_MAE: prenomes FEMININO + sobrenomes TODOS; pares=$PairCount; seed=$($Seed + 1)." -ForegroundColor Yellow
    Write-Host 'Nascimento: permanece condicionado ao blocking.' -ForegroundColor Yellow
    Write-Host 'Sem análise de regressão contra modelo anterior.' -ForegroundColor Yellow
    Write-Host "Transcript: $TranscriptPath" -ForegroundColor Yellow

    $env:JORNADA_LINKAGE_IBGE_MC_PAIR_COUNT = [string]$PairCount
    $env:JORNADA_LINKAGE_IBGE_MC_SEED = [string]$Seed

    $total = 10

    Write-Step 1 $total 'Restore da solução' 'dotnet restore Jornada.sln'
    Invoke-Native 'dotnet' @('restore','Jornada.sln') 'dotnet restore'

    Write-Step 2 $total 'Build Release com warnings como erro' 'dotnet build Jornada.sln --configuration Release --no-restore -warnaserror'
    Invoke-Native 'dotnet' @('build','Jornada.sln','--configuration','Release','--no-restore','-warnaserror') 'dotnet build'

    Write-Step 3 $total 'Testes unitários' 'dotnet test .\tests\Jornada.Tests\Jornada.Tests.csproj --configuration Release --no-build --filter TestCategory=Unit'
    Invoke-Native 'dotnet' @(
        'test',
        '.\tests\Jornada.Tests\Jornada.Tests.csproj',
        '--configuration','Release',
        '--no-build',
        '--filter','TestCategory=Unit'
    ) 'dotnet test Unit'

    Write-Step 4 $total 'Limpar cluster local' '.\scripts\local-cluster.ps1 -Action clean'
    Invoke-LocalScript $Cluster @{ Action = 'clean' } 'local-cluster clean'

    Write-Step 5 $total 'Subir cluster canônico' '.\scripts\local-cluster.ps1 -Action up'
    Invoke-LocalScript $Cluster @{ Action = 'up' } 'local-cluster up'

    Write-Step 6 $total 'Calibrar e ativar o modelo DEV' '.\scripts\local-cluster.ps1 -Action calibrate'
    Invoke-LocalScript $Cluster @{ Action = 'calibrate' } 'local-cluster calibrate'

    Write-Step 7 $total 'Reproduzir Monte Carlo IBGE nominal' ".\scripts\local-ibge-u-bootstrap.ps1 -PairCount $PairCount -Seed $Seed"
    Invoke-LocalScript $IbgeReport @{ PairCount = $PairCount; Seed = $Seed } 'local-ibge-u-bootstrap'

    Write-Step 8 $total 'Executar linkage' '.\scripts\local-cluster.ps1 -Action linkage'
    Invoke-LocalScript $Cluster @{ Action = 'linkage' } 'local-cluster linkage'

    Write-Step 9 $total 'Diagnosticar modelo e run' '.\scripts\local-cluster.ps1 -Action linkage-diagnose'
    Invoke-LocalScript $Cluster @{ Action = 'linkage-diagnose' } 'local-cluster linkage-diagnose'

    Write-Step 10 $total 'Validação independente e DEV quality gate' '.\scripts\local-linkage-validation.ps1'
    Invoke-LocalScript $Validation @{} 'local-linkage-validation'

    Write-Host ''
    Write-Host 'LINKAGE MONTE CARLO DEV VALIDATION: OK' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host 'LINKAGE MONTE CARLO DEV VALIDATION: FALHOU' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    throw
}
finally {
    $env:JORNADA_LINKAGE_IBGE_MC_PAIR_COUNT = $previousPairCount
    $env:JORNADA_LINKAGE_IBGE_MC_SEED = $previousSeed

    if ($transcriptStarted) {
        try { Stop-Transcript | Out-Null } catch {}
    }

    Write-Host ''
    Write-Host "Transcript completo: $TranscriptPath"
}
