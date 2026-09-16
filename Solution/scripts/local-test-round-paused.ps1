param()

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Wait-ForNextStep {
    param([Parameter(Mandatory = $true)][string]$CompletedStep)
    Write-Host ''
    Write-Host "OK: $CompletedStep" -ForegroundColor Green
    [void](Read-Host 'Pressione ENTER para executar o próximo comando')
    Write-Host ''
}

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][scriptblock]$Command
    )

    Write-Host '============================================================'
    Write-Host $Title -ForegroundColor Cyan
    Write-Host '============================================================'
    $global:LASTEXITCODE = 0
    & $Command
    if (-not $?) {
        throw "Falha em: $Title"
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Falha em: $Title (exit code $LASTEXITCODE)"
    }
    Wait-ForNextStep $Title
}

Push-Location $Root
try {
    Write-Host 'RODADA LOCAL DE TESTES - EXECUÇÃO PASSO A PASSO' -ForegroundColor Yellow
    Write-Host "Solution: $Root"
    Write-Host 'O script para na primeira falha e pausa entre todos os comandos.'
    Write-Host ''

    Invoke-Step '1/18 - Estado atual do Git' {
        git status --short --branch
    }

    Invoke-Step '2/18 - Restore locked' {
        dotnet restore Jornada.sln --locked-mode
    }

    Invoke-Step '3/18 - Build Release com warnings como erro' {
        dotnet build Jornada.sln --configuration Release --no-restore -warnaserror
    }

    Invoke-Step '4/18 - Testes não-integration completos' {
        dotnet test .\tests\Jornada.Tests\Jornada.Tests.csproj --configuration Release --no-build --filter 'TestCategory!=Integration'
    }

    Invoke-Step '5/18 - Reset do banco local canônico' {
        & .\scripts\local-db.ps1 -Action reset
    }

    Invoke-Step '6/18 - Core local oficial' {
        & .\scripts\local-test.ps1
    }

    Invoke-Step '7/18 - Upgrade DDL e idempotência' {
        & .\scripts\local-ddl-upgrade.ps1
    }

    Invoke-Step '8/18 - E2E HTTP -> Bronze -> Silver -> Gold -> Serving -> HTTP' {
        & .\scripts\local-e2e.ps1
    }

    Invoke-Step '9/18 - Fault injection' {
        & .\scripts\local-fault-injection.ps1
    }

    Invoke-Step '10/18 - Cluster clean' {
        & .\scripts\local-cluster.ps1 -Action clean
    }

    Invoke-Step '11/18 - Cluster up' {
        & .\scripts\local-cluster.ps1 -Action up
    }

    Invoke-Step '12/18 - Calibração' {
        & .\scripts\local-cluster.ps1 -Action calibrate
    }

    Invoke-Step '13/18 - Linkage' {
        & .\scripts\local-cluster.ps1 -Action linkage
    }

    Invoke-Step '14/18 - Diagnóstico tie-aware do Linkage' {
        & .\scripts\local-cluster.ps1 -Action linkage-diagnose
    }

    Invoke-Step '15/18 - Smoke de escala' {
        & .\scripts\local-scale.ps1 -Profile smoke
    }

    Invoke-Step '16/18 - Restaurar banco canônico após SCALE' {
        & .\scripts\local-db.ps1 -Action reset
    }

    Invoke-Step '17/18 - Suíte completa agregada' {
        & .\scripts\local-test-all.ps1 -Suite full
    }

    Invoke-Step '18/18 - Estado final do Git' {
        git status --short --branch
    }

    Write-Host 'RODADA LOCAL COMPLETA: OK' -ForegroundColor Green
}
finally {
    Pop-Location
}
