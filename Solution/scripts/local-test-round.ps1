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
    Write-Host 'RODADA LOCAL DE TESTES - EXECUÇÃO CONTÍNUA' -ForegroundColor Yellow
    Write-Host "Solution: $Root"
    Write-Host 'O script imprime cada comando como comentário, executa sem pausa e para na primeira falha.'
    Write-Host 'A suíte agregada local-test-all é propositalmente separada para não repetir toda a rodada.'
    Write-Host ''

    Invoke-Step '1/19 - Estado atual do Git' 'git status --short --branch' { git status --short --branch }
    Invoke-Step '2/19 - Restore locked' 'dotnet restore Jornada.sln --locked-mode' { dotnet restore Jornada.sln --locked-mode }
    Invoke-Step '3/19 - Build Release com warnings como erro' 'dotnet build Jornada.sln --configuration Release --no-restore -warnaserror' { dotnet build Jornada.sln --configuration Release --no-restore -warnaserror }
    Invoke-Step '4/19 - Testes não-integration completos' "dotnet test .\tests\Jornada.Tests\Jornada.Tests.csproj --configuration Release --no-build --filter 'TestCategory!=Integration'" { dotnet test .\tests\Jornada.Tests\Jornada.Tests.csproj --configuration Release --no-build --filter 'TestCategory!=Integration' }
    Invoke-Step '5/19 - Reset do banco local canônico' '.\scripts\local-db.ps1 -Action reset' { & .\scripts\local-db.ps1 -Action reset }
    Invoke-Step '6/19 - Core local oficial' '.\scripts\local-test.ps1' { & .\scripts\local-test.ps1 }
    Invoke-Step '7/19 - Upgrade DDL e idempotência' '.\scripts\local-ddl-upgrade.ps1' { & .\scripts\local-ddl-upgrade.ps1 }
    Invoke-Step '8/19 - E2E HTTP -> Bronze -> Silver -> Gold -> Serving -> HTTP' '.\scripts\local-e2e.ps1' { & .\scripts\local-e2e.ps1 }
    Invoke-Step '9/19 - Fault injection' '.\scripts\local-fault-injection.ps1' { & .\scripts\local-fault-injection.ps1 }
    Invoke-Step '10/19 - Cluster clean' '.\scripts\local-cluster.ps1 -Action clean' { & .\scripts\local-cluster.ps1 -Action clean }
    Invoke-Step '11/19 - Cluster up' '.\scripts\local-cluster.ps1 -Action up' { & .\scripts\local-cluster.ps1 -Action up }
    Invoke-Step '12/19 - Calibração' '.\scripts\local-cluster.ps1 -Action calibrate' { & .\scripts\local-cluster.ps1 -Action calibrate }
    Invoke-Step '13/19 - Linkage' '.\scripts\local-cluster.ps1 -Action linkage' { & .\scripts\local-cluster.ps1 -Action linkage }
    Invoke-Step '14/19 - Diagnóstico tie-aware do Linkage' '.\scripts\local-cluster.ps1 -Action linkage-diagnose' { & .\scripts\local-cluster.ps1 -Action linkage-diagnose }
    Invoke-Step '15/19 - Evaluation read-only + candidate ranking + blocking por passe' '.\scripts\local-linkage-evaluation-smoke.ps1' { & .\scripts\local-linkage-evaluation-smoke.ps1 }
    Invoke-Step '16/19 - Encerrar cluster antes do SCALE' '.\scripts\local-cluster.ps1 -Action clean' { & .\scripts\local-cluster.ps1 -Action clean }
    Invoke-Step '17/19 - Smoke de escala + probe sincronizado de coordenação' '.\scripts\local-scale.ps1 -Profile smoke' { & .\scripts\local-scale.ps1 -Profile smoke }
    Invoke-Step '18/19 - Restaurar banco canônico após SCALE' '.\scripts\local-db.ps1 -Action reset' { & .\scripts\local-db.ps1 -Action reset }
    Invoke-Step '19/19 - Estado final do Git' 'git status --short --branch' { git status --short --branch }

    Write-Host 'RODADA LOCAL CONTÍNUA: OK' -ForegroundColor Green
    Write-Host 'Suíte agregada opcional, em comando separado: .\scripts\local-test-all.ps1 -Suite full'
}
finally {
    Pop-Location
}