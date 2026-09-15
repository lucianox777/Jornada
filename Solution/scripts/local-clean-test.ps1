[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ClusterScript = Join-Path $PSScriptRoot 'local-cluster.ps1'

if (-not (Test-Path -LiteralPath $ClusterScript)) {
    throw "Script do cluster local não encontrado: $ClusterScript"
}

function Invoke-ClusterStep {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('clean','up','calibrate','linkage','linkage-diagnose')]
        [string]$Action
    )

    Write-Host ''
    Write-Host "=== local-cluster.ps1 $Action ==="
    & $ClusterScript $Action
    if ($LASTEXITCODE -ne 0) {
        throw "local-cluster.ps1 $Action falhou ($LASTEXITCODE)."
    }
}

Push-Location $Root
try {
    $branch = (& git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível determinar a branch Git atual.' }

    $sha = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível determinar o SHA Git atual.' }

    Write-Host 'Teste limpo local de calibração + linkage'
    Write-Host "Branch: $branch"
    Write-Host "SHA:    $sha"
    Write-Host 'Este teste remove os volumes Docker locais, reconstrói as imagens e recria a massa sintética.'

    $watch = [System.Diagnostics.Stopwatch]::StartNew()

    Invoke-ClusterStep 'clean'
    Invoke-ClusterStep 'up'
    Invoke-ClusterStep 'calibrate'
    Invoke-ClusterStep 'linkage'
    Invoke-ClusterStep 'linkage-diagnose'

    $watch.Stop()
    Write-Host ''
    Write-Host ("Teste limpo concluído com sucesso em {0:n1} min." -f $watch.Elapsed.TotalMinutes)
}
finally {
    Pop-Location
}
