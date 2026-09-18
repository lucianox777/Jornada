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

if ($content -match "N''(?:ref\.|U''|NOME''|SOBRENOME''|BRASIL''|UF''|MUNICIPIO'')") {
    throw 'Diagnostico IBGE voltou a duplicar aspas SQL dentro de here-string literal.'
}

$args = @()
if ($NoStart) { $args += '-NoStart' }

Write-Host '# .\scripts\local-diagnose-ibge-reference.ps1' + $(if ($NoStart) { ' -NoStart' } else { '' })
& $Target @args
if ($LASTEXITCODE -ne 0) { throw "local-diagnose-ibge-reference.ps1 falhou ($LASTEXITCODE)." }

Write-Host 'LOCAL IBGE DIAGNOSTIC TEST: OK' -ForegroundColor Green
