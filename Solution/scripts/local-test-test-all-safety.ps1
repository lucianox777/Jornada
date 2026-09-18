[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Target = Join-Path $PSScriptRoot 'local-test-all.ps1'
$CurrentPowerShell = (Get-Process -Id $PID).Path

if (-not (Test-Path -LiteralPath $Target -PathType Leaf)) {
    throw "Script alvo nao encontrado: $Target"
}

$content = Get-Content -LiteralPath $Target -Raw -Encoding UTF8
$null = [scriptblock]::Create($content)

foreach ($required in @(
    '[switch]$AllowDestructiveReset',
    'if (-not $AllowDestructiveReset)',
    '-AllowDestructiveReset -IsolatedExecution',
    "Invoke-PowerShellScript 'local-db.ps1' @('-Action', 'reset')"
)) {
    if (-not $content.Contains($required)) {
        throw "Contrato de seguranca ausente em local-test-all.ps1: $required"
    }
}

$stdoutPath = Join-Path ([IO.Path]::GetTempPath()) ("jornada-test-all-guard-{0}.out" -f [Guid]::NewGuid().ToString('N'))
$stderrPath = Join-Path ([IO.Path]::GetTempPath()) ("jornada-test-all-guard-{0}.err" -f [Guid]::NewGuid().ToString('N'))

try {
    Write-Host '# local-test-all.ps1 -Suite standard  # esperado: abortar sem tocar no banco'
    $process = Start-Process -FilePath $CurrentPowerShell -ArgumentList @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $Target,
        '-Suite',
        'standard'
    ) -Wait -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath

    $stdout = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Raw } else { '' }
    $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { '' }
    $combined = $stdout + "`n" + $stderr

    if ($process.ExitCode -eq 0) {
        throw 'local-test-all.ps1 sem -AllowDestructiveReset deveria falhar antes de qualquer acao.'
    }
    if (-not $combined.Contains('Reset destrutivo nao autorizado')) {
        throw 'Falha esperada nao confirmou o guard de reset destrutivo.'
    }
    foreach ($forbidden in @(
        'git fetch',
        "local-db.ps1 '-Action' reset",
        'Criando worktree isolado',
        'Atualizando referencia origin/master'
    )) {
        if ($combined.Contains($forbidden)) {
            throw "Guard executou acao proibida antes de abortar: $forbidden"
        }
    }

    Write-Host 'LOCAL TEST ALL DESTRUCTIVE GUARD: OK' -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $stdoutPath,$stderrPath -Force -ErrorAction SilentlyContinue
}
