param(
    [switch]$SkipRestore,
    [switch]$SkipBuild,
    [switch]$FullUnit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Invoke-NativeStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    Write-Host ''
    Write-Host "--- $Name ---"
    Write-Host "# $Command"
    $global:LASTEXITCODE = 0
    & $Action
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        throw "$Name falhou ($code)."
    }
}

$TargetedFilter = 'FullyQualifiedName~NominalDfBenchmarkCalibrationTests|FullyQualifiedName~NominalDfCalibrationDatasetTests|FullyQualifiedName~SplinkCompatibleTermFrequencyTests|FullyQualifiedName~IbgeNominalBenchmarkTests'

Push-Location $Root
try {
    if (-not $SkipRestore) {
        Invoke-NativeStep 'Restore locked' 'dotnet restore Jornada.sln --locked-mode' {
            dotnet restore Jornada.sln --locked-mode
        }
    }

    if (-not $SkipBuild) {
        Invoke-NativeStep 'Build Release com warnings como erro' 'dotnet build Jornada.sln --configuration Release --no-restore -warnaserror' {
            dotnet build Jornada.sln --configuration Release --no-restore -warnaserror
        }
    }

    Invoke-NativeStep 'Testes direcionados DF / benchmark / IBGE' "dotnet test .\tests\Jornada.Tests\Jornada.Tests.csproj --configuration Release --no-build --filter `"$TargetedFilter`"" {
        dotnet test .\tests\Jornada.Tests\Jornada.Tests.csproj --configuration Release --no-build --filter $TargetedFilter
    }

    if ($FullUnit) {
        Invoke-NativeStep 'Todos os testes nao-integration' 'dotnet test .\tests\Jornada.Tests\Jornada.Tests.csproj --configuration Release --no-build --filter "TestCategory!=Integration"' {
            dotnet test .\tests\Jornada.Tests\Jornada.Tests.csproj --configuration Release --no-build --filter 'TestCategory!=Integration'
        }
    }

    Write-Host ''
    Write-Host 'LOCAL DF CALIBRATION TEST: OK' -ForegroundColor Green
    Write-Host ''
    Write-Host 'Fechamento local recomendado antes do merge:'
    Write-Host '# .\scripts\local-test-all.ps1 -Suite full'
}
finally {
    Pop-Location
}
