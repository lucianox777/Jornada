﻿[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Get-Location).Path,
    [switch]$RunTests
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path $RepositoryRoot).Path
$testProject = Join-Path $repo 'tests\Jornada.Integration.Tests\Jornada.Integration.Tests.csproj'
$bundleRoot = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path $testProject)) {
    throw "Não encontrei tests\Jornada.Integration.Tests\Jornada.Integration.Tests.csproj em '$repo'. Execute a partir da raiz da Jornada ou informe -RepositoryRoot."
}

$sourceIntegration = Join-Path $bundleRoot 'tests\Jornada.Integration.Tests\Integration'
if (-not (Test-Path $sourceIntegration)) {
    throw "Pacote inválido: não encontrei '$sourceIntegration'."
}

$targetIntegration = Join-Path $repo 'tests\Jornada.Integration.Tests\Integration'
New-Item -ItemType Directory -Force -Path $targetIntegration | Out-Null

Write-Host 'Aplicando infraestrutura de Integration - Solution Engenharia v4.01...'
Copy-Item -Path (Join-Path $sourceIntegration '*') -Destination $targetIntegration -Recurse -Force

# Materializa a dependência no projeto real. O dotnet CLI preserva o formato do projeto e
# é compatível com PackageReference local/Central Package Management.
$packageListing = dotnet list $testProject package --format json | Out-String
if ($LASTEXITCODE -ne 0) { throw 'dotnet list package falhou.' }

if ($packageListing -notmatch 'Testcontainers\.MsSql') {
    Write-Host 'Adicionando Testcontainers.MsSql 4.14.0 ao projeto de testes...'
    dotnet add $testProject package Testcontainers.MsSql --version 4.14.0 --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet add package falhou.' }
} else {
    Write-Host 'Testcontainers.MsSql já está referenciado no projeto.'
}

Write-Host 'Atualizando restore/lock files...'
dotnet restore $testProject --use-lock-file --force-evaluate
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore falhou.' }

Write-Host 'Validando build Release...'
dotnet build $testProject --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet build falhou.' }

if ($RunTests) {
    if ([string]::IsNullOrWhiteSpace($env:JORNADA_TEST_SQL_CONNECTION)) {
        docker info | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw 'Docker não está disponível. Inicie Docker Desktop (Linux containers) ou forneça JORNADA_TEST_SQL_CONNECTION.'
        }
    }

    Write-Host 'Executando Integration...'
    dotnet test $testProject --configuration Release --no-build --logger 'trx;LogFileName=integration-testcontainers.trx'
    if ($LASTEXITCODE -ne 0) { throw 'Integration falhou.' }
}

Write-Host ''
Write-Host 'Implementação aplicada.'
Write-Host 'Sem JORNADA_TEST_SQL_CONNECTION, Integration sobe SQL Server 2022 CU26 fixado por digest.'
Write-Host 'Com JORNADA_TEST_SQL_CONNECTION, a suíte cria banco exclusivo por execução por padrão.'
Write-Host 'Para banco externo já dedicado, use JORNADA_TEST_SQL_USE_EXISTING_DATABASE=true explicitamente.'
