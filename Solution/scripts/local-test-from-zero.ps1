param(
    [ValidateSet('standard','full')]
    [string]$Suite = 'full'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Target = Join-Path $PSScriptRoot 'local-test-all.ps1'
if (-not (Test-Path -LiteralPath $Target -PathType Leaf)) {
    throw "local-test-all.ps1 nao encontrado: $Target"
}

Write-Warning 'FROM ZERO: este comando recria o banco/volumes de teste e rematerializa a referencia IBGE.'
Write-Host '# .\scripts\local-test-all.ps1 -Suite ' + $Suite + ' -FromZero -AllowDestructiveReset'
& $Target -Suite $Suite -FromZero -AllowDestructiveReset
