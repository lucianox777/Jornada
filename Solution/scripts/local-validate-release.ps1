param()

$ErrorActionPreference = 'Stop'
$ScriptVersion = '2026.09.02-v3.90'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Solution = Join-Path $Root 'Jornada.sln'
$UnitProject = Join-Path $Root 'tests\Jornada.Tests\Jornada.Tests.csproj'
$IntegrationProject = Join-Path $Root 'tests\Jornada.Integration.Tests\Jornada.Integration.Tests.csproj'
$UnitDll = Join-Path $Root 'tests\Jornada.Tests\bin\Release\net8.0\Jornada.Tests.dll'
$IntegrationDll = Join-Path $Root 'tests\Jornada.Integration.Tests\bin\Release\net8.0\Jornada.Integration.Tests.dll'

function Invoke-Checked {
    param(
        [Parameter(Mandatory=$true)][string]$Command,
        [Parameter(Mandatory=$true)][string[]]$CommandArgs,
        [Parameter(Mandatory=$true)][string]$Step
    )

    Write-Host ''
    Write-Host "=== $Step ==="
    & $Command @CommandArgs

    if ($LASTEXITCODE -ne 0) {
        throw "$Step falhou (exit code $LASTEXITCODE)."
    }
}

function Assert-FileExists {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description não foi gerado: $Path"
    }

    $file = Get-Item -LiteralPath $Path
    Write-Host "$Description OK: $($file.FullName)"
    Write-Host "Tamanho: $($file.Length) bytes"
}

function Test-DockerEngine {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        return $false
    }

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & docker info --format '{{.ServerVersion}}' *> $null
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return ($exitCode -eq 0)
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK não encontrado no PATH.'
}

foreach ($required in @($Solution, $UnitProject, $IntegrationProject)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Arquivo obrigatório não encontrado: $required"
    }
}

$slnText = Get-Content -LiteralPath $Solution -Raw
if ($slnText -notmatch 'Jornada\.Integration\.Tests') {
    throw "Jornada.sln não contém Jornada.Integration.Tests. Use a Solution completa desta release."
}

$previousSqlConnection = $env:JORNADA_TEST_SQL_CONNECTION
$previousUseExisting = $env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE

Push-Location $Root
try {
    Write-Host '==================================================='
    Write-Host ' Jornada - validação local Release'
    Write-Host " ScriptVersion: $ScriptVersion"
    Write-Host '==================================================='
    Write-Host "Raiz: $Root"
    Write-Host "dotnet: $(& dotnet --version)"
    Write-Host 'Solution contém Jornada.Integration.Tests: OK'

    Invoke-Checked `
        -Command 'dotnet' `
        -CommandArgs @('restore', $Solution, '--locked-mode') `
        -Step '1/9 Restore locked da Solution'

    # Garante project.assets.json após local-clean.ps1.
    Invoke-Checked `
        -Command 'dotnet' `
        -CommandArgs @('restore', $UnitProject, '--locked-mode') `
        -Step '2/9 Restore locked Jornada.Tests'

    Invoke-Checked `
        -Command 'dotnet' `
        -CommandArgs @('restore', $IntegrationProject, '--locked-mode') `
        -Step '3/9 Restore locked Jornada.Integration.Tests'

    Invoke-Checked `
        -Command 'dotnet' `
        -CommandArgs @(
            'build', $Solution,
            '--configuration', 'Release',
            '--no-restore',
            '-warnaserror'
        ) `
        -Step '4/9 Build Release da Solution'

    Invoke-Checked `
        -Command 'dotnet' `
        -CommandArgs @(
            'build', $UnitProject,
            '--configuration', 'Release',
            '--no-restore',
            '-warnaserror'
        ) `
        -Step '5/9 Build explícito Jornada.Tests'

    Assert-FileExists -Path $UnitDll -Description 'DLL Unit'

    Invoke-Checked `
        -Command 'dotnet' `
        -CommandArgs @(
            'test', $UnitProject,
            '--configuration', 'Release',
            '--no-build',
            '--no-restore'
        ) `
        -Step '6/9 Testes Unit'

    Write-Host ''
    Write-Host '=== 7/9 Verificando Docker para Integration ==='
    if (-not (Test-DockerEngine)) {
        throw 'Docker Engine não está em execução ou não está acessível. Inicie o Docker Desktop antes dos testes de integração.'
    }
    Write-Host 'Docker Engine: OK'

    Invoke-Checked `
        -Command 'dotnet' `
        -CommandArgs @(
            'build', $IntegrationProject,
            '--configuration', 'Release',
            '--no-restore',
            '-warnaserror'
        ) `
        -Step '8/9 Build explícito Jornada.Integration.Tests'

    Assert-FileExists -Path $IntegrationDll -Description 'DLL Integration'

    # Força o caminho descartável Testcontainers. A fixture cria
    # JornadaIntegrationTest_<guid>, que satisfaz os guards Test/Dev/Local.
    Remove-Item Env:JORNADA_TEST_SQL_CONNECTION -ErrorAction SilentlyContinue
    Remove-Item Env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE -ErrorAction SilentlyContinue

    Invoke-Checked `
        -Command 'dotnet' `
        -CommandArgs @(
            'test', $IntegrationProject,
            '--configuration', 'Release',
            '--no-build',
            '--no-restore'
        ) `
        -Step '9/9 Testes Integration'

    Write-Host ''
    Write-Host '==================================================='
    Write-Host 'VALIDAÇÃO LOCAL RELEASE CONCLUÍDA COM SUCESSO'
    Write-Host "ScriptVersion:        $ScriptVersion"
    Write-Host 'Restore Solution:     OK'
    Write-Host 'Restore Unit:         OK'
    Write-Host 'Restore Integration:  OK'
    Write-Host 'Build Solution:       OK'
    Write-Host 'Build Unit:           OK'
    Write-Host 'Testes Unit:          OK'
    Write-Host 'Docker:               OK'
    Write-Host 'Build Integration:    OK'
    Write-Host 'Testes Integration:   OK'
    Write-Host '==================================================='
}
finally {
    if ([string]::IsNullOrWhiteSpace($previousSqlConnection)) {
        Remove-Item Env:JORNADA_TEST_SQL_CONNECTION -ErrorAction SilentlyContinue
    }
    else {
        $env:JORNADA_TEST_SQL_CONNECTION = $previousSqlConnection
    }

    if ([string]::IsNullOrWhiteSpace($previousUseExisting)) {
        Remove-Item Env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE -ErrorAction SilentlyContinue
    }
    else {
        $env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE = $previousUseExisting
    }

    Pop-Location
}
