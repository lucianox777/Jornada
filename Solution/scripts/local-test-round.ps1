cls

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string]$CommandText,
        [Parameter(Mandatory = $true)][scriptblock]$Command
    )

    Write-Host '============================================================'
    Write-Host $Title -ForegroundColor Cyan
    Write-Host '============================================================'
    Write-Host "# $CommandText" -ForegroundColor DarkGray
    $global:LASTEXITCODE = 0
    & $Command
    if (-not $?) { throw "Falha em: $Title" }
    if ($LASTEXITCODE -ne 0) { throw "Falha em: $Title (exit code $LASTEXITCODE)" }
    Write-Host "OK: $Title" -ForegroundColor Green
    Write-Host ''
}

Push-Location $Root
try {
    Write-Host 'RODADA LOCAL DE TESTES - MODO PRESERVADOR' -ForegroundColor Yellow
    Write-Host "Solution: $Root"
    Write-Host 'O banco compartilhado e a referencia IBGE sao preservados por padrao.'
    Write-Host 'E2E usa banco isolado JornadaE2E; scale/clean ficam no fluxo from-zero explicito.'
    Write-Host 'Gate histórico pré-HML opcional: .\scripts\local-ddl-upgrade.ps1'
    Write-Host ''

    Invoke-Step '1/14 - Estado atual do Git' 'git status --short --branch' { git status --short --branch }
    Invoke-Step '2/14 - Quick check IBGE read-only' '.\scripts\local-check-ibge-reference.ps1' { & .\scripts\local-check-ibge-reference.ps1 }
    Invoke-Step '3/14 - Restore locked' 'dotnet restore Jornada.sln --locked-mode' { dotnet restore Jornada.sln --locked-mode }
    Invoke-Step '4/14 - Build Release com warnings como erro' 'dotnet build Jornada.sln --configuration Release --no-restore -warnaserror' { dotnet build Jornada.sln --configuration Release --no-restore -warnaserror }
    Invoke-Step '5/14 - Testes nao-integration completos' "dotnet test .\tests\Jornada.Tests\Jornada.Tests.csproj --configuration Release --no-build --filter 'TestCategory!=Integration'" { dotnet test .\tests\Jornada.Tests\Jornada.Tests.csproj --configuration Release --no-build --filter 'TestCategory!=Integration' }
    Invoke-Step '6/14 - Core local oficial' '.\scripts\local-test.ps1' { & .\scripts\local-test.ps1 }
    Invoke-Step '7/14 - E2E isolado HTTP -> Bronze -> Silver -> Gold -> Serving -> HTTP' '.\scripts\local-e2e.ps1' { & .\scripts\local-e2e.ps1 }
    Invoke-Step '8/14 - Fault injection' '.\scripts\local-fault-injection.ps1' { & .\scripts\local-fault-injection.ps1 }
    Invoke-Step '9/14 - Cluster up preservando volumes' '.\scripts\local-cluster.ps1 -Action up' { & .\scripts\local-cluster.ps1 -Action up }
    Invoke-Step '10/14 - Calibracao' '.\scripts\local-cluster.ps1 -Action calibrate' { & .\scripts\local-cluster.ps1 -Action calibrate }
    Invoke-Step '11/14 - Linkage' '.\scripts\local-cluster.ps1 -Action linkage' { & .\scripts\local-cluster.ps1 -Action linkage }
    Invoke-Step '12/14 - Diagnostico tie-aware do Linkage' '.\scripts\local-cluster.ps1 -Action linkage-diagnose' { & .\scripts\local-cluster.ps1 -Action linkage-diagnose }
    Invoke-Step '13/14 - Evaluation read-only + candidate ranking + blocking por passe' '.\scripts\local-linkage-evaluation-smoke.ps1' { & .\scripts\local-linkage-evaluation-smoke.ps1 }
    Invoke-Step '14/14 - Quick check IBGE final' '.\scripts\local-check-ibge-reference.ps1 -NoStart' { & .\scripts\local-check-ibge-reference.ps1 -NoStart }

    Write-Host 'RODADA LOCAL CONTINUA: OK' -ForegroundColor Green
    Write-Host 'Suíte agregada preservadora: .\scripts\local-test-all.ps1 -Suite full'
    Write-Host 'Instalacao limpa/scale destrutivo: .\scripts\local-test-from-zero.ps1 -Suite full -AllowDestructiveReset'
}
finally {
    Pop-Location
}
