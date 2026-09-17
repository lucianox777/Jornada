[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ShellGate = Join-Path $PSScriptRoot 'local-ddl-upgrade.sh'
$CurrentDdl = Join-Path $Root 'database/Jornada_Fase1.sql'

if (-not (Test-Path -LiteralPath $ShellGate)) {
    throw "Gate DDL canônico não encontrado: $ShellGate"
}
if (-not (Test-Path -LiteralPath $CurrentDdl)) {
    throw "DDL corrente não encontrado: $CurrentDdl"
}

function Get-BashExecutable {
    $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1

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

    $bash = Get-Command bash -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
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

function Assert-CurrentBronzeIndexContract {
    $ddl = Get-Content -LiteralPath $CurrentDdl -Raw -Encoding UTF8
    $safeCreate = 'CREATE INDEX IX_bronze_entrega_arquivo_payload_sha256 ON bronze.entrega_arquivo(payload_sha256)'
    $unsafeCreate = 'CREATE INDEX IX_bronze_entrega_arquivo_objeto_chave ON bronze.entrega_arquivo(objeto_chave)'
    $legacyDrop = 'DROP INDEX IX_bronze_entrega_arquivo_objeto_chave ON bronze.entrega_arquivo'

    if (-not $ddl.Contains($safeCreate)) {
        throw 'DDL corrente não contém o índice seguro IX_bronze_entrega_arquivo_payload_sha256.'
    }
    if ($ddl.Contains($unsafeCreate)) {
        throw 'DDL corrente voltou a criar o índice legado de objeto_chave com chave potencial de 2048 bytes.'
    }
    if (-not $ddl.Contains($legacyDrop)) {
        throw 'DDL corrente não contém a remoção explícita do índice legado de objeto_chave.'
    }
}

$bashExe = Get-BashExecutable
if ([string]::IsNullOrWhiteSpace($bashExe)) {
    throw 'Bash compatível não encontrado. No Windows, este gate exige o Git Bash do Git for Windows e não usa automaticamente o launcher do WSL.'
}
Write-Host "Bash selecionado para o gate DDL: $bashExe"
Assert-CurrentBronzeIndexContract
Write-Host 'Índice Bronze corrente: OK (SHA-256 como chave; objeto_chave fora da chave do índice).' -ForegroundColor Green

# Git Bash/MSYS converte argumentos POSIX enviados a executáveis Windows. Sem estas exclusões,
# `docker compose exec -w /workspace ... /opt/mssql-tools18/bin/sqlcmd` reescreve caminhos que
# existem somente dentro do container Linux para caminhos do host (por exemplo
# C:/Program Files/Git/opt/mssql-tools18/bin/sqlcmd). Excluímos somente os caminhos internos do
# container e preservamos a conversão normal dos demais caminhos do host.
$requiredArgConvExclusions = @('/workspace', '/opt/mssql-tools18/bin/sqlcmd')
$previousArgConvExcl = $env:MSYS2_ARG_CONV_EXCL
if ($env:OS -eq 'Windows_NT') {
    foreach ($argExclusion in $requiredArgConvExclusions) {
        if ([string]::IsNullOrWhiteSpace($env:MSYS2_ARG_CONV_EXCL)) {
            $env:MSYS2_ARG_CONV_EXCL = $argExclusion
        }
        elseif (($env:MSYS2_ARG_CONV_EXCL -split ';') -notcontains $argExclusion) {
            $env:MSYS2_ARG_CONV_EXCL = "$($env:MSYS2_ARG_CONV_EXCL);$argExclusion"
        }
    }
}

Push-Location $Root
try {
    $historicalWarningSeen = $false
    $skipHistoricalContinuation = $false

    & $bashExe ./scripts/local-ddl-upgrade.sh 2>&1 | ForEach-Object {
        $line = $_.ToString()

        if ($line -like "*The index 'IX_bronze_entrega_arquivo_objeto_chave' has maximum length of 2048 bytes*") {
            if (-not $historicalWarningSeen) {
                Write-Host 'INFO: baseline histórico v3.65 contém o índice antigo de 2048 bytes; aviso omitido nesta rodada. O DDL corrente já usa o índice SHA-256 e remove o legado.' -ForegroundColor DarkGray
                $historicalWarningSeen = $true
            }
            $skipHistoricalContinuation = $true
            return
        }

        if ($skipHistoricalContinuation -and $line -like 'For some combination of large values, the insert/update operation will fail*') {
            $skipHistoricalContinuation = $false
            return
        }

        $skipHistoricalContinuation = $false
        Write-Host $line
    }

    $gateExitCode = $LASTEXITCODE
    if ($gateExitCode -ne 0) {
        throw "local-ddl-upgrade.sh falhou ($gateExitCode)."
    }
}
finally {
    Pop-Location
    if ($env:OS -eq 'Windows_NT') {
        if ($null -eq $previousArgConvExcl) {
            Remove-Item Env:MSYS2_ARG_CONV_EXCL -ErrorAction SilentlyContinue
        }
        else {
            $env:MSYS2_ARG_CONV_EXCL = $previousArgConvExcl
        }
    }
}
