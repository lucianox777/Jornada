param(
    [switch]$SkipRestore,
    [switch]$SkipBuild,
    [switch]$Quick,
    [switch]$Offline,
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

$TargetedFilter = if ($Quick) {
    'FullyQualifiedName~NominalDfBenchmarkCalibrationTests'
}
else {
    'FullyQualifiedName~NominalDfBenchmarkCalibrationTests|FullyQualifiedName~NominalDfCalibrationDatasetTests|FullyQualifiedName~SplinkCompatibleTermFrequencyTests|FullyQualifiedName~IbgeNominalBenchmarkTests'
}

Push-Location $Root
try {
    if (-not $Offline) {
        Write-Host ''
        Write-Host '--- Check rapido da referencia IBGE existente ---'
        Write-Host '# .\scripts\local-check-ibge-reference.ps1'
        # O check é read-only e nunca materializa/recarrega a referência.
        # Em caso de divergência, falha fechado antes de gastar tempo em build/testes.
        & (Join-Path $PSScriptRoot 'local-check-ibge-reference.ps1')
    }
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

    $testStepName = if ($Quick) { 'Teste rapido DF validation/test (snapshot em memoria)' } else { 'Testes direcionados DF / benchmark / IBGE' }
    Invoke-NativeStep $testStepName "dotnet test .\tests\Jornada.Tests\Jornada.Tests.csproj --configuration Release --no-build --filter `"$TargetedFilter`"" {
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
    Write-Host 'Validacao ampla preservando a referencia IBGE ja carregada:'
    Write-Host '# .\scripts\local-test.ps1'
    Write-Host ''
    Write-Host 'Fechamento amplo preservando a referencia IBGE:'
    Write-Host '# .\scripts\local-test-all.ps1 -Suite full'
    Write-Host ''
    Write-Host 'Instalacao limpa/from-zero (destrutiva; use apenas quando esse gate for necessario):'
    Write-Host '# .\scripts\local-test-from-zero.ps1 -Suite full'
}
finally {
    Pop-Location
}
