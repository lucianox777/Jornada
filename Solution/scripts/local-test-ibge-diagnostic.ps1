[CmdletBinding()]
param(
    [switch]$NoStart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Target = Join-Path $PSScriptRoot 'local-diagnose-ibge-reference.ps1'
if (-not (Test-Path -LiteralPath $Target -PathType Leaf)) { throw "Script alvo nao encontrado: $Target" }

$content = Get-Content -LiteralPath $Target -Raw -Encoding UTF8
$null = [scriptblock]::Create($content)

foreach ($required in @(
    'function Wait-SqlReady',
    'function Wait-DatabaseOnline',
    "if (`$state -eq 'ONLINE') { return }",
    'DIAGNOSTICO CONCLUIDO: nenhuma alteracao foi feita.'
)) {
    if (-not $content.Contains($required)) {
        throw "Contrato do diagnostico IBGE ausente: $required"
    }
}

foreach ($forbidden in @(
    "N''ref.",
    "N''U''",
    "N''NOME''",
    "N''SOBRENOME''",
    "N''BRASIL''",
    "N''UF''",
    "N''MUNICIPIO''"
)) {
    if ($content.Contains($forbidden)) {
        throw "Diagnostico IBGE voltou a duplicar aspas SQL dentro de here-string literal: $forbidden"
    }
}

$args = @()
if ($NoStart) { $args += '-NoStart' }

$display = '.\scripts\local-diagnose-ibge-reference.ps1'
if ($NoStart) { $display += ' -NoStart' }
Write-Host "# $display"
& $Target @args

Write-Host 'LOCAL IBGE DIAGNOSTIC TEST: OK' -ForegroundColor Green
