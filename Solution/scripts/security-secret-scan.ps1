[CmdletBinding()]
param(
    [switch]$CurrentTreeOnly
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$outDir = Join-Path $repoRoot 'Solution\.local\gitleaks'
$config = Join-Path $repoRoot '.gitleaks.toml'

if (-not (Get-Command gitleaks -ErrorAction SilentlyContinue)) {
    throw 'gitleaks não encontrado no PATH. Instale uma versão atual do Gitleaks antes de executar a varredura.'
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function Invoke-GitleaksScan {
    param(
        [Parameter(Mandatory=$true)][string[]]$Arguments,
        [Parameter(Mandatory=$true)][string]$Label
    )

    & gitleaks @Arguments
    $exit = $LASTEXITCODE
    if ($exit -eq 0) {
        Write-Host "${Label}: OK"
        return 0
    }
    if ($exit -eq 1) {
        Write-Host "${Label}: ACHADOS — consulte o relatório local redigido em Solution/.local/gitleaks." -ForegroundColor Yellow
        return 1
    }
    throw "gitleaks falhou em '$Label' com exit code $exit."
}

Push-Location $repoRoot
try {
    & gitleaks version

    $treeReport = Join-Path $outDir 'current-tree.json'
    $treeExit = Invoke-GitleaksScan -Label 'Árvore corrente' -Arguments @(
        'dir',
        '--config', $config,
        '--redact=100',
        '--report-format', 'json',
        '--report-path', $treeReport,
        '.'
    )

    $historyExit = 0
    if (-not $CurrentTreeOnly) {
        $historyReport = Join-Path $outDir 'git-history.json'
        $historyExit = Invoke-GitleaksScan -Label 'Histórico Git completo' -Arguments @(
            'git',
            '--config', $config,
            '--redact=100',
            '--report-format', 'json',
            '--report-path', $historyReport,
            '--log-opts=--all',
            '.'
        )
    }

    if ($treeExit -ne 0 -or $historyExit -ne 0) {
        throw 'Gitleaks encontrou ocorrências. Classifique cada achado; não reescreva o histórico e não crie baseline/allowlist automaticamente.'
    }

    Write-Host 'SECRET SCAN: OK' -ForegroundColor Green
}
finally {
    Pop-Location
}
