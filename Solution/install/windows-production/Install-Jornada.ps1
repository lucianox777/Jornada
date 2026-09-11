[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$ConfigPath,
    [Parameter(Mandatory = $true)] [string]$PayloadRoot,
    [switch]$ValidateOnly,
    [switch]$PlanOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$configFull = (Resolve-Path -LiteralPath $ConfigPath).Path
$payloadFull = (Resolve-Path -LiteralPath $PayloadRoot).Path
$config = Get-Content -Raw -Encoding UTF8 $configFull | ConvertFrom-Json
$coreInstaller = Join-Path $PSScriptRoot 'Install-JornadaProduction.ps1'
$migrationRunner = Join-Path $PSScriptRoot 'Invoke-JornadaMigrationLedger.ps1'
$migrationRoot = Join-Path $payloadFull 'database\migrations'

if (-not (Test-Path -LiteralPath $coreInstaller)) { throw "Instalador core ausente: $coreInstaller" }
if (-not (Test-Path -LiteralPath $migrationRunner)) { throw "Runner de migrações ausente: $migrationRunner" }

$initializeDatabase = [bool]$config.sql.initializeDatabase
if ($initializeDatabase) {
    & $migrationRunner `
        -ConnectionString ([string]$config.sql.connectionString) `
        -MigrationRoot $migrationRoot `
        -ValidateOnly
    if ($LASTEXITCODE -ne 0) { throw 'Manifesto/runner de migrações inválido.' }
}

$coreArgs = @('-ConfigPath', $configFull, '-PayloadRoot', $payloadFull)
if ($ValidateOnly) { $coreArgs += '-ValidateOnly' }
if ($PlanOnly) { $coreArgs += '-PlanOnly' }
& $coreInstaller @coreArgs
if ($LASTEXITCODE -ne 0) { throw "Instalador core falhou. ExitCode=$LASTEXITCODE" }

if ($ValidateOnly -or $PlanOnly -or -not $initializeDatabase) { exit 0 }

# Compatibilidade: o core mantém a criação/aplicação do DDL base já existente no
# instalador. O manifesto normativo é aplicado imediatamente depois e passa a ser
# a fonte única de ordem/checksum para upgrades. Cada migração + registro no ledger
# é atômico no runner PowerShell.
& $migrationRunner `
    -ConnectionString ([string]$config.sql.connectionString) `
    -MigrationRoot $migrationRoot
if ($LASTEXITCODE -ne 0) { throw "Aplicação do ledger de migrações falhou. ExitCode=$LASTEXITCODE" }

Write-Host 'Instalação/upgrade com ledger de migrações: OK'
