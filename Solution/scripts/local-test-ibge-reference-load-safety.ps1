param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$LoadScript = Join-Path $PSScriptRoot 'local-load-ibge-reference.ps1'
$CheckScript = Join-Path $PSScriptRoot 'local-check-ibge-reference.ps1'

foreach ($path in @($LoadScript,$CheckScript)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Script nao encontrado: $path" }
    $content = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    $null = [scriptblock]::Create($content)
}

$load = Get-Content -LiteralPath $LoadScript -Raw -Encoding UTF8
$check = Get-Content -LiteralPath $CheckScript -Raw -Encoding UTF8

foreach ($required in @(
    '[switch]$AllowLoad',
    'if (-not $AllowLoad)',
    "Invoke-Compose -ComposeArgs @('up','-d','sqlserver')",
    "Invoke-Compose -ComposeArgs @('build','jornada-reference-bootstrap')",
    "Invoke-Compose -ComposeArgs @('run','--rm','--no-deps','jornada-reference-bootstrap')",
    'local-check-ibge-reference.ps1',
    'Carga IBGE nao autorizada'
)) {
    if (-not $load.Contains($required)) { throw "Contrato de carga IBGE ausente: $required" }
}

foreach ($forbidden in @(
    'local-db.ps1',
    "'reset'",
    "'clean'",
    "down','-v",
    'local-cluster.ps1'
)) {
    if ($load.Contains($forbidden)) { throw "Carga IBGE nao pode executar operacao destrutiva: $forbidden" }
}

foreach ($required in @(
    'function Wait-SqlReady',
    'function Wait-DatabaseOnline',
    'IBGE REFERENCE QUICK CHECK: OK',
    'projection-manifest.json'
)) {
    if (-not $check.Contains($required)) { throw "Contrato do quick check IBGE ausente: $required" }
}

if ($check.Contains('if ($canonicalHash -match ''^[0-9A-Fa-f]{64}if')) {
    throw 'Quick check IBGE contem concatenacao SQL/PowerShell corrompida.'
}

Write-Host 'LOCAL IBGE REFERENCE LOAD SAFETY TEST: OK' -ForegroundColor Green
