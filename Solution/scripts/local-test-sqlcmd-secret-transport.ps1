# Regression mock: SQLCMDPASSWORD must reach sqlcmd through the child environment,
# never through docker compose command-line arguments, and must be restored on error.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tempEnv = Join-Path ([IO.Path]::GetTempPath()) ('jornada-sqlcmd-mock-' + [guid]::NewGuid().ToString('N') + '.env')
$previousEnvFile = [Environment]::GetEnvironmentVariable('JORNADA_LOCAL_ENV_FILE', 'Process')
$previousSqlcmdPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
$global:MockPassword = 'Synthetic_Mock_Only!2026'
$global:MockDockerCalls = 0
$global:MockDockerShouldFail = $false

function global:docker {
    $received = @($args | ForEach-Object { [string]$_ })
    if ($received -contains 'ps') {
        $global:LASTEXITCODE = 0
        return 'mock-sqlserver-container'
    }
    $global:MockDockerCalls++
    if ($received -notcontains 'exec') { throw 'Expected docker compose exec.' }
    $envIndex = [array]::IndexOf($received, '-e')
    if ($envIndex -lt 0 -or ($envIndex + 1) -ge $received.Length -or
        $received[$envIndex + 1] -cne 'SQLCMDPASSWORD') {
        throw 'docker compose must forward SQLCMDPASSWORD by variable name only.'
    }
    foreach ($arg in $received) {
        if ($arg.StartsWith('SQLCMDPASSWORD=') -or $arg.Contains($global:MockPassword)) {
            throw 'Credential was exposed in docker process arguments.'
        }
    }
    if ($env:SQLCMDPASSWORD -cne $global:MockPassword) {
        throw 'Docker did not inherit the expected SQLCMDPASSWORD value.'
    }
    if ($global:MockDockerShouldFail) { throw 'Synthetic docker failure.' }
    $global:LASTEXITCODE = 0
}

try {
    [IO.File]::WriteAllText(
        $tempEnv,
        "JORNADA_SQL_SA_PASSWORD=$($global:MockPassword)`nJORNADA_SQL_DATABASE=JornadaSyntheticDev`n",
        (New-Object System.Text.UTF8Encoding($false))
    )
    $env:JORNADA_LOCAL_ENV_FILE = $tempEnv
    $env:SQLCMDPASSWORD = 'PARENT_SCOPE_SENTINEL'

    foreach ($scriptName in @('local-linkage-operational-metrics.ps1', 'local-synthetic-diagnostics.ps1')) {
        $global:MockDockerCalls = 0
        & (Join-Path $PSScriptRoot $scriptName) -EnvFile $tempEnv -DatabaseName 'JornadaSyntheticDev'
        if ($global:MockDockerCalls -ne 1) { throw "$scriptName must execute precisely one mock sqlcmd command." }
        if ($env:SQLCMDPASSWORD -cne 'PARENT_SCOPE_SENTINEL') {
            throw "$scriptName did not restore the caller's SQLCMDPASSWORD."
        }
    }

    $global:MockDockerCalls = 0
    & (Join-Path $PSScriptRoot 'local-sql-runtime-smoke.ps1')
    if ($global:MockDockerCalls -ne 1) { throw 'SQL runtime smoke did not execute its mock sqlcmd command.' }
    if ($env:SQLCMDPASSWORD -cne 'PARENT_SCOPE_SENTINEL') {
        throw 'SQL runtime smoke did not restore the caller environment.'
    }

    $global:MockDockerShouldFail = $true
    $failedAsExpected = $false
    try {
        & (Join-Path $PSScriptRoot 'local-linkage-operational-metrics.ps1') -EnvFile $tempEnv
    }
    catch {
        if ($_.Exception.Message -notlike '*Synthetic docker failure*') { throw }
        $failedAsExpected = $true
    }
    if (-not $failedAsExpected) { throw 'Expected a synthetic sqlcmd failure.' }
    if ($env:SQLCMDPASSWORD -cne 'PARENT_SCOPE_SENTINEL') {
        throw 'SQLCMDPASSWORD leaked to the parent after a failure.'
    }
    Write-Host 'SQLCMD SECRET TRANSPORT/RESTORE MOCK: OK'
}
finally {
    $global:MockDockerShouldFail = $false
    Remove-Item -LiteralPath $tempEnv -ErrorAction SilentlyContinue
    if ($null -eq $previousEnvFile) {
        Remove-Item Env:\JORNADA_LOCAL_ENV_FILE -ErrorAction SilentlyContinue
    } else {
        $env:JORNADA_LOCAL_ENV_FILE = $previousEnvFile
    }
    if ($null -eq $previousSqlcmdPassword) {
        Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
    } else {
        $env:SQLCMDPASSWORD = $previousSqlcmdPassword
    }
    Remove-Item Function:\docker -ErrorAction SilentlyContinue
    Remove-Variable MockPassword, MockDockerCalls, MockDockerShouldFail -Scope Global -ErrorAction SilentlyContinue
}
