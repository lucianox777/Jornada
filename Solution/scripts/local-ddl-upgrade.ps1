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
    $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue

    # No Windows, `bash` no PATH pode ser C:\Windows\System32\bash.exe (launcher do WSL).
    # Este gate foi escrito para Git Bash e precisa operar sobre os mesmos caminhos/CLI do host
    # Windows (Docker Desktop, dotnet, arquivos C:\...). Portanto resolvemos Git Bash primeiro e
    # não delegamos implicitamente ao WSL.
    if ($env:OS -eq 'Windows_NT') {
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

    $bash = Get-Command bash -CommandType Application -ErrorAction SilentlyContinue
    if ($null -ne $bash) { return $bash.Source }

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
    throw 'Bash compatível não encontrado. No Windows, este gate exige o Git Bash do Git for Windows e não usa automaticamente o launcher do WSL.'
}
Write-Host "Bash selecionado para o gate DDL: $bashExe"

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
