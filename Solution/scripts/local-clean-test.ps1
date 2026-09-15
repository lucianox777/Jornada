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

function Invoke-GitCapture {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = @(& git @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') falhou ($LASTEXITCODE)."
    }
    return $output
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

$originalBranch = $null
$originalSha = $null
$stashCommit = $null
$stashCreated = $false

Push-Location $Root
try {
    $originalBranch = ((Invoke-GitCapture @('branch','--show-current')) -join '').Trim()
    $originalSha = ((Invoke-GitCapture @('rev-parse','HEAD')) -join '').Trim()
    if ([string]::IsNullOrWhiteSpace($originalSha)) {
        throw 'Não foi possível determinar o SHA Git original.'
    }

    $dirty = @(Invoke-GitCapture @('status','--porcelain','--untracked-files=normal'))
    if ($dirty.Count -gt 0) {
        $stashLabel = "jornada-local-clean-test-$([Guid]::NewGuid().ToString('N'))"
        Write-Host 'Alterações locais detectadas; preservando automaticamente em stash temporário...'
        Invoke-Git @('stash','push','-u','-m',$stashLabel)
        $stashCommit = ((Invoke-GitCapture @('rev-parse','--verify','refs/stash')) -join '').Trim()
        if ([string]::IsNullOrWhiteSpace($stashCommit)) {
            throw 'O Git informou stash criado, mas não foi possível identificar o commit do stash.'
        }
        $stashCreated = $true
        Write-Host "Stash temporário: $stashCommit"
    }

    Write-Host 'Atualizando fonte canônico antes do teste...'
    Invoke-Git @('fetch','origin','master')
    Invoke-Git @('checkout','master')
    Invoke-Git @('pull','--ff-only','origin','master')

    $branch = ((Invoke-GitCapture @('branch','--show-current')) -join '').Trim()
    if ($branch -ne 'master') {
        throw "Branch canônica esperada após atualização: master; atual='$branch'."
    }

    $sha = ((Invoke-GitCapture @('rev-parse','HEAD')) -join '').Trim()
    if ([string]::IsNullOrWhiteSpace($sha)) {
        throw 'Não foi possível determinar o SHA Git atual.'
    }

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
    try {
        if (-not [string]::IsNullOrWhiteSpace($originalSha)) {
            $currentBranch = ((Invoke-GitCapture @('branch','--show-current')) -join '').Trim()
            if (-not [string]::IsNullOrWhiteSpace($originalBranch)) {
                if ($currentBranch -ne $originalBranch) {
                    Write-Host ''
                    Write-Host "Restaurando branch original '$originalBranch'..."
                    Invoke-Git @('checkout',$originalBranch)
                }
            }
            else {
                $currentSha = ((Invoke-GitCapture @('rev-parse','HEAD')) -join '').Trim()
                if ($currentSha -ne $originalSha -or -not [string]::IsNullOrWhiteSpace($currentBranch)) {
                    Write-Host ''
                    Write-Host "Restaurando HEAD destacado original $originalSha..."
                    Invoke-Git @('checkout','--detach',$originalSha)
                }
            }
        }

        if ($stashCreated -and -not [string]::IsNullOrWhiteSpace($stashCommit)) {
            Write-Host 'Restaurando alterações locais preservadas...'
            & git stash apply --index $stashCommit
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "A restauração automática encontrou conflito. O stash foi PRESERVADO em $stashCommit. Resolva os conflitos manualmente; nada foi descartado."
                throw "Não foi possível restaurar automaticamente o stash $stashCommit."
            }

            $stashEntry = @(Invoke-GitCapture @('stash','list','--format=%gd %H')) |
                Where-Object { $_ -match "\s$([regex]::Escape($stashCommit))$" } |
                Select-Object -First 1

            if ($null -ne $stashEntry) {
                $stashRef = ($stashEntry -split '\s+', 2)[0]
                Invoke-Git @('stash','drop',$stashRef)
                Write-Host 'Alterações locais restauradas; stash temporário removido.'
            }
            else {
                Write-Warning "Alterações locais foram aplicadas, mas o stash $stashCommit não foi localizado para remoção automática. Verifique 'git stash list'."
            }
        }
    }
    finally {
        Pop-Location
    }
}
