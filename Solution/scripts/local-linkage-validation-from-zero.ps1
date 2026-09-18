param(
    [ValidateRange(10000,5000000)]
    [int]$PairCount = 1000000,

    [int]$Seed = 20260917
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Cluster = Join-Path $PSScriptRoot 'local-cluster.ps1'
$IbgeReport = Join-Path $PSScriptRoot 'local-ibge-u-bootstrap.ps1'
$Validation = Join-Path $PSScriptRoot 'local-linkage-validation.ps1'

function Invoke-Step {
    param(
        [Parameter(Mandatory=$true)][string]$Title,
        [Parameter(Mandatory=$true)][string]$CommandText,
        [Parameter(Mandatory=$true)][scriptblock]$Action
    )

    Write-Host ''
    Write-Host "=== $Title ===" -ForegroundColor Cyan
    Write-Host "# $CommandText" -ForegroundColor DarkGray
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Title falhou ($LASTEXITCODE)."
    }
}

$previousPairCount = $env:JORNADA_LINKAGE_IBGE_MC_PAIR_COUNT
$previousSeed = $env:JORNADA_LINKAGE_IBGE_MC_SEED

try {
    Write-Host "# Set-Location '$Root'" -ForegroundColor DarkGray
    Set-Location -LiteralPath $Root

    $env:JORNADA_LINKAGE_IBGE_MC_PAIR_COUNT = [string]$PairCount
    $env:JORNADA_LINKAGE_IBGE_MC_SEED = [string]$Seed

    Write-Host ''
    Write-Host 'Validação linkage DEV a partir de cluster limpo.' -ForegroundColor Yellow
    Write-Host "Monte Carlo IBGE: pares_por_campo=$PairCount; seed_pessoa=$Seed; seed_mae=$($Seed+1)." -ForegroundColor Yellow
    Write-Host 'NOME: prenomes TODOS + sobrenomes TODOS.' -ForegroundColor Yellow
    Write-Host 'NOME_MAE: prenomes FEMININO + sobrenomes TODOS.' -ForegroundColor Yellow
    Write-Host 'Nascimento: permanece condicionado ao blocking.' -ForegroundColor Yellow
    Write-Host 'Sem análise de regressão contra modelo anterior.' -ForegroundColor Yellow

    Invoke-Step '1/7 - Limpar cluster local' '.\scripts\local-cluster.ps1 -Action clean' {
        & $Cluster -Action clean
    }

    Invoke-Step '2/7 - Subir cluster canônico' '.\scripts\local-cluster.ps1 -Action up' {
        & $Cluster -Action up
    }

    Invoke-Step '3/7 - Calibrar RASCUNHO -> VALIDADO -> ATIVO' '.\scripts\local-cluster.ps1 -Action calibrate' {
        & $Cluster -Action calibrate
    }

    Invoke-Step '4/7 - Reproduzir Monte Carlo IBGE read-only' ".\scripts\local-ibge-u-bootstrap.ps1 -PairCount $PairCount -Seed $Seed" {
        & $IbgeReport -PairCount $PairCount -Seed $Seed
    }

    Invoke-Step '5/7 - Executar linkage' '.\scripts\local-cluster.ps1 -Action linkage' {
        & $Cluster -Action linkage
    }

    Invoke-Step '6/7 - Diagnosticar modelo e run' '.\scripts\local-cluster.ps1 -Action linkage-diagnose' {
        & $Cluster -Action linkage-diagnose
    }

    Invoke-Step '7/7 - Validação independente com quality gate DEV' '.\scripts\local-linkage-validation.ps1' {
        & $Validation
    }

    Write-Host ''
    Write-Host 'LINKAGE DEV VALIDATION FROM ZERO: OK' -ForegroundColor Green
}
finally {
    $env:JORNADA_LINKAGE_IBGE_MC_PAIR_COUNT = $previousPairCount
    $env:JORNADA_LINKAGE_IBGE_MC_SEED = $previousSeed
}
