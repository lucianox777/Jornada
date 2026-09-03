param(
    [Parameter(Mandatory=$true)][string]$Version,
    [string]$ReleaseInfoPath
)
$ErrorActionPreference='Stop'
$RepoRoot=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $ReleaseInfoPath) { $ReleaseInfoPath=Join-Path $RepoRoot 'RELEASE_INFO.txt' }
$normalized=$Version.TrimStart('v')
$tag="jornada-solution-v$normalized"
python3 (Join-Path $RepoRoot 'Solution/scripts/release-source-gate.py') `
    --repo $RepoRoot `
    --release-info $ReleaseInfoPath `
    --expected-tag $tag `
    --require-clean
if ($LASTEXITCODE -ne 0) { throw 'Falha na validação de RELEASE_INFO/tag.' }
Write-Host 'RELEASE_INFO versionado e tag verificados; nenhuma geração pós-tag foi realizada.'
