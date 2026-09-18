Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Target = Join-Path $PSScriptRoot 'local-test-all.ps1'
$FromZero = Join-Path $PSScriptRoot 'local-test-from-zero.ps1'
$CurrentPowerShell = (Get-Process -Id $PID).Path

foreach ($path in @($Target,$FromZero)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Script alvo nao encontrado: $path" }
    $null = [scriptblock]::Create((Get-Content -LiteralPath $path -Raw -Encoding UTF8))
}

$content = Get-Content -LiteralPath $Target -Raw -Encoding UTF8
$wrapper = Get-Content -LiteralPath $FromZero -Raw -Encoding UTF8

foreach ($required in @(
    '[switch]$FromZero',
    '[switch]$AllowDestructiveReset',
    'if ($FromZero -and -not $AllowDestructiveReset)',
    'if (-not $FromZero -and $AllowDestructiveReset)',
    "Invoke-PowerShellScript 'local-check-ibge-reference.ps1' @()",
    'SKIPPED_PRESERVE_IBGE',
    "Invoke-PowerShellScript 'local-db.ps1' @('-Action', 'reset')",
    "Invoke-ClusterAction 'clean'",
    "mode = $(if ($FromZero) { 'FROM_ZERO_DESTRUCTIVE' } else { 'PRESERVE_IBGE' })"
)) {
    if (-not $content.Contains($required)) { throw "Contrato de seguranca ausente em local-test-all.ps1: $required" }
}

foreach ($required in @('-FromZero','-AllowDestructiveReset','local-test-all.ps1')) {
    if (-not $wrapper.Contains($required)) { throw "Wrapper from-zero perdeu contrato: $required" }
}

# A prova dinamica cobre somente a barreira destrutiva: -FromZero sem autorizacao
# deve abortar antes de fetch/worktree/reset. O modo padrao nao e executado aqui
# porque ele agora e a suite preservadora real e pode ser demorado.
$stdoutPath = Join-Path ([IO.Path]::GetTempPath()) ("jornada-from-zero-guard-{0}.out" -f [Guid]::NewGuid().ToString('N'))
$stderrPath = Join-Path ([IO.Path]::GetTempPath()) ("jornada-from-zero-guard-{0}.err" -f [Guid]::NewGuid().ToString('N'))

try {
    $process = Start-Process -FilePath $CurrentPowerShell -ArgumentList @(
        '-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',$Target,
        '-Suite','standard','-FromZero'
    ) -Wait -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath

    $stdout = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Raw } else { '' }
    $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { '' }
    $combined = $stdout + [Environment]::NewLine + $stderr

    if ($process.ExitCode -eq 0) { throw 'FromZero sem autorizacao deveria falhar antes de qualquer acao.' }
    if (-not $combined.Contains('Reconstrucao from-zero nao autorizada')) {
        throw 'Falha esperada nao confirmou a barreira from-zero.'
    }
    foreach ($forbidden in @('git fetch','Criando worktree isolado',"local-db.ps1 '-Action' reset")) {
        if ($combined.Contains($forbidden)) { throw "Guard from-zero executou acao proibida: $forbidden" }
    }

    Write-Host 'LOCAL TEST ALL PRESERVE/FROM-ZERO CONTRACT: OK' -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $stdoutPath,$stderrPath -Force -ErrorAction SilentlyContinue
}
