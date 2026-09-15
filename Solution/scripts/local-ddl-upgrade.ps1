[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ShellGate = Join-Path $PSScriptRoot 'local-ddl-upgrade.sh'

if (-not (Test-Path -LiteralPath $ShellGate)) {
    throw "Gate DDL canônico não encontrado: $ShellGate"
}

function Get-BashExecutable {
    $bash = Get-Command bash -ErrorAction SilentlyContinue
    if ($null -ne $bash) { return $bash.Source }

    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git) { return $null }

    $gitCmdDir = Split-Path -Parent $git.Source
    $gitRoot = Split-Path -Parent $gitCmdDir
    foreach ($candidate in @(
        (Join-Path $gitRoot 'bin/bash.exe'),
        (Join-Path $gitRoot 'usr/bin/bash.exe')
    )) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

$bashExe = Get-BashExecutable
if ([string]::IsNullOrWhiteSpace($bashExe)) {
    throw 'Bash não encontrado. No Windows, instale/use o Git for Windows; o PowerShell localiza automaticamente o Git Bash.'
}

Push-Location $Root
try {
    & $bashExe ./scripts/local-ddl-upgrade.sh
    if ($LASTEXITCODE -ne 0) {
        throw "local-ddl-upgrade.sh falhou ($LASTEXITCODE)."
    }
}
finally {
    Pop-Location
}
