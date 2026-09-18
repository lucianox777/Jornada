[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$TestAll = Join-Path $PSScriptRoot 'local-test-all.ps1'
$FromZero = Join-Path $PSScriptRoot 'local-test-from-zero.ps1'
$CurrentPowerShell = (Get-Process -Id $PID).Path

foreach ($path in @($TestAll,$FromZero)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Script nao encontrado: $path" }
    $null = [scriptblock]::Create((Get-Content -LiteralPath $path -Raw -Encoding UTF8))
}

$testAll = Get-Content -LiteralPath $TestAll -Raw -Encoding UTF8
$fromZeroContent = Get-Content -LiteralPath $FromZero -Raw -Encoding UTF8

foreach ($required in @(
    "Invoke-PowerShellScript 'local-check-ibge-reference.ps1'",
    "Invoke-PowerShellScript 'local-e2e.ps1'",
    "Invoke-ClusterAction 'up'",
    'ibgeReferencePreserved = $true',
    'destructiveReset = $false'
)) {
    if (-not $testAll.Contains($required)) { throw "Contrato preservador ausente em local-test-all.ps1: $required" }
}

foreach ($forbidden in @(
    '[switch]$AllowDestructiveReset',
    "Invoke-PowerShellScript 'local-db.ps1' @('-Action', 'reset')",
    "Invoke-ClusterAction 'clean'",
    "Invoke-PowerShellScript 'local-scale.ps1'"
)) {
    if ($testAll.Contains($forbidden)) { throw "local-test-all.ps1 voltou a conter operacao destrutiva: $forbidden" }
}

foreach ($required in @(
    '[switch]$AllowDestructiveReset',
    'if (-not $AllowDestructiveReset)',
    "Invoke-Script 'local-cluster.ps1' @('-Action','clean')",
    "Invoke-Script 'local-db.ps1' @('-Action','reset')",
    "Invoke-Script 'local-scale.ps1' @('-Profile','smoke')",
    "Invoke-Script 'local-load-ibge-reference.ps1' @('-AllowLoad')"
)) {
    if (-not $fromZeroContent.Contains($required)) { throw "Contrato destrutivo ausente em local-test-from-zero.ps1: $required" }
}

$e2e = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'local-e2e.ps1') -Raw -Encoding UTF8
$db = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'local-db.ps1') -Raw -Encoding UTF8
foreach ($required in @(
    '[switch]$AllowSharedDatabaseReset',
    '$usesIsolatedDatabase = -not $AllowSharedDatabaseReset',
    "'JornadaE2E'",
    '-NoSyntheticCorpus -DatabaseName $db'
)) {
    if (-not $e2e.Contains($required)) { throw "Contrato de isolamento E2E ausente: $required" }
}
if (-not $db.Contains('[string]$DatabaseName')) {
    throw 'local-db.ps1 perdeu suporte a banco isolado por nome explicito.'
}

$guardIndex = $fromZeroContent.IndexOf('if (-not $AllowDestructiveReset)')
$firstActionIndex = $fromZeroContent.IndexOf("Invoke-Script 'local-cluster.ps1' @('-Action','clean')")
if ($guardIndex -lt 0 -or $firstActionIndex -lt 0 -or $guardIndex -ge $firstActionIndex) {
    throw 'Guard from-zero deve aparecer antes da primeira acao destrutiva.'
}

Write-Host '# local-test-from-zero.ps1 -Suite standard  # esperado: abortar sem tocar no ambiente'
$caught = $null
try {
    & $FromZero -Suite standard
}
catch {
    $caught = $_.Exception.Message
}

if ($caught -ne 'Reset destrutivo nao autorizado.') {
    throw "Guard from-zero retornou falha inesperada: $caught"
}

Write-Host 'LOCAL TEST PRESERVATION/FROM-ZERO GUARD: OK' -ForegroundColor Green
