[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ClusterScript = Join-Path $PSScriptRoot 'local-cluster.ps1'

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git não encontrado no PATH.'
}
if (-not (Test-Path -LiteralPath $ClusterScript)) {
    throw "Script do cluster local não encontrado: $ClusterScript"
}

function Invoke-Git {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') falhou ($LASTEXITCODE)."
    }
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
    $dirty = @(& git status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível verificar o estado da árvore Git.' }
    if ($dirty.Count -gt 0) {
        throw 'Teste limpo bloqueado: há alterações locais não commitadas. Faça commit ou stash antes de continuar.'
    }

    Write-Host 'Atualizando fonte canônico antes do teste...'
    Invoke-Git @('fetch','origin','master')
    Invoke-Git @('checkout','master')
    Invoke-Git @('pull','--ff-only','origin','master')

    $branch = (& git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or $branch -ne 'master') {
        throw "Branch canônica esperada após atualização: master; atual='$branch'."
    }

    $sha = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível determinar o SHA Git atual.' }

    Write-Host ''
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
