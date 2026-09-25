# Exercises the real synthetic calibration entrypoint in a temporary fixture with
# mocked PowerShell functions. No Docker, dotnet, real .env, SQL or cleanup runs.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sourceScript = Join-Path $PSScriptRoot 'local-synthetic-calibration.ps1'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('jornada-synth-sql-mock-' + [guid]::NewGuid().ToString('N'))
$fixtureScripts = Join-Path $tempRoot 'scripts'
$fixtureScript = Join-Path $fixtureScripts 'local-synthetic-calibration.ps1'
$global:MockSecret = 'Synthetic_PowerShell_SQL_Mock_2026!'
$global:MockFailAt = 'none'
$global:MockTrace = New-Object 'System.Collections.Generic.List[string]'

$envNames = @(
    'SQLCMDPASSWORD',
    'JORNADA_LOCAL_ENV_FILE',
    'JORNADA_SYNTH_PSEUDONYMIZATION_KEY',
    'ConnectionStrings__Jornada',
    'Database__Provider',
    'Ensaio__Mode',
    'Ensaio__SyntheticCalibration__WaveCount',
    'Ensaio__SyntheticCalibration__People',
    'Ensaio__SyntheticCalibration__Seed',
    'Ensaio__SyntheticCalibration__ExpectedSeeds',
    'Ensaio__SyntheticCalibration__RunGroupId',
    'Ensaio__SyntheticCalibration__ErrorProfile',
    'Ensaio__SyntheticCalibration__DataReferencia',
    'Ensaio__Endpoints__IngestaoEntregas'
)
$original = @{}
foreach ($name in $envNames) {
    $original[$name] = [Environment]::GetEnvironmentVariable($name,'Process')
}

function global:docker {
    $argv = @($args | ForEach-Object { [string]$_ })
    $index = [array]::IndexOf($argv,'-e')
    if ($argv -notcontains 'exec' -or $index -lt 0 -or $index + 1 -ge $argv.Count -or
        $argv[$index + 1] -cne 'SQLCMDPASSWORD') {
        throw 'Docker must forward only the SQLCMDPASSWORD name.'
    }
    foreach ($argument in $argv) {
        if ($argument.StartsWith('SQLCMDPASSWORD=') -or $argument.Contains($global:MockSecret)) {
            throw 'Synthetic SQL password leaked through Docker argv.'
        }
    }
    if ($env:SQLCMDPASSWORD -cne $global:MockSecret) {
        throw 'Docker did not inherit SQLCMDPASSWORD.'
    }
    $phase = if ($argv -contains 'database/Jornada_Dev_SyntheticCalibration_ExclusivePreflight.sql') {
        'PREFLIGHT'
    } elseif ($argv -contains 'database/Jornada_Dev_SyntheticCalibration_Cleanup.sql') {
        'CLEANUP'
    } else {
        throw 'Unexpected SQL command executed by calibration.'
    }
    $global:MockTrace.Add($phase)
    if ($global:MockFailAt -eq 'throw' -and $phase -eq 'PREFLIGHT') {
        throw 'Synthetic Docker invocation exception.'
    }
    if (($global:MockFailAt -eq 'preflight' -and $phase -eq 'PREFLIGHT') -or
        ($global:MockFailAt -eq 'cleanup' -and $phase -eq 'CLEANUP')) {
        $global:LASTEXITCODE = 29
    } else {
        $global:LASTEXITCODE = 0
    }
}

function global:dotnet {
    if ($global:MockFailAt -ne 'none') { throw 'dotnet must not run after a SQL failure.' }
    $argv = @($args | ForEach-Object { [string]$_ })
    if ($argv -notcontains 'run' -or $argv -notcontains 'src/Jornada.Ensaio') {
        throw 'Unexpected dotnet invocation in the isolated fixture.'
    }
    if (-not $env:ConnectionStrings__Jornada.Contains($global:MockSecret)) {
        throw 'dotnet did not inherit the synthetic connection string.'
    }
    $global:MockTrace.Add('DOTNET')
    $global:LASTEXITCODE = 0
}

try {
    New-Item -ItemType Directory -Force -Path $fixtureScripts | Out-Null
    Copy-Item -LiteralPath $sourceScript -Destination $fixtureScript
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllLines((Join-Path $tempRoot '.env'), @(
        'JORNADA_SQL_DATABASE=JornadaLocal',
        'JORNADA_SQL_SA_PASSWORD=Unused_Canonical_Test_Only'
    ), $utf8)
    [IO.File]::WriteAllLines((Join-Path $tempRoot '.env.synthetic.local'), @(
        'JORNADA_SQL_DATABASE=JornadaSyntheticDev',
        ('JORNADA_SQL_SA_PASSWORD=' + $global:MockSecret),
        'JORNADA_SQL_PORT=14333'
    ), $utf8)
    $fakeDb = @'
param(
    [Parameter(Position=0)][string]$Action,
    [switch]$NoSyntheticCorpus,
    [string]$DatabaseName
)
if ($Action -ne 'up' -or -not $NoSyntheticCorpus -or $DatabaseName -ne 'JornadaSyntheticDev') {
    throw 'A real/shared database was requested in the mock.'
}
$parent = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')
if ($null -ne $parent -and $parent -cne 'PARENT_SCOPE_SENTINEL') {
    throw 'The prior SQLCMDPASSWORD was changed before database bootstrap.'
}
$global:MockTrace.Add('DB')
$global:LASTEXITCODE = 0
'@
    [IO.File]::WriteAllText((Join-Path $fixtureScripts 'local-db.ps1'),$fakeDb,$utf8)

    $env:JORNADA_LOCAL_ENV_FILE = Join-Path $tempRoot '.env.synthetic.local'
    $env:JORNADA_SYNTH_PSEUDONYMIZATION_KEY = 'Synthetic_Test_Only_Pseudonymization_Key'
    $cases = @(
        @{ Mode='none';     Expected=@('DB','PREFLIGHT','CLEANUP','DOTNET'); Error='' },
        @{ Mode='preflight';Expected=@('DB','PREFLIGHT');           Error='Preflight recusou' },
        @{ Mode='cleanup';  Expected=@('DB','PREFLIGHT','CLEANUP'); Error='Limpeza sintética' },
        @{ Mode='throw';    Expected=@('DB','PREFLIGHT');           Error='Synthetic Docker invocation exception.' }
    )
    foreach ($case in $cases) {
        $global:MockTrace.Clear()
        $global:MockFailAt = $case.Mode
        $env:SQLCMDPASSWORD = 'PARENT_SCOPE_SENTINEL'
        $caught = $false
        try {
            & $fixtureScript -People 10 -Seed 42 -Waves 3
        } catch {
            if ($case.Mode -eq 'none') { throw }
            if (-not $_.Exception.Message.Contains($case.Error)) { throw }
            $caught = $true
        }
        if ($case.Mode -ne 'none' -and -not $caught) {
            throw "Calibration did not stop for $($case.Mode)."
        }
        if (($global:MockTrace -join ',') -cne ($case.Expected -join ',')) {
            throw "Unexpected sequence for $($case.Mode): $($global:MockTrace -join ',')."
        }
        if ($env:SQLCMDPASSWORD -cne 'PARENT_SCOPE_SENTINEL') {
            throw "Parent SQLCMDPASSWORD not restored after $($case.Mode)."
        }
    }

    $global:MockTrace.Clear()
    $global:MockFailAt = 'none'
    Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
    & $fixtureScript -People 10 -Seed 42 -Waves 3
    if ($null -ne [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')) {
        throw 'Absent original SQLCMDPASSWORD must stay absent.'
    }
    if (($global:MockTrace -join ',') -cne 'DB,PREFLIGHT,CLEANUP,DOTNET') {
        throw 'No-parent calibration sequence differed.'
    }
    $global:LASTEXITCODE = 0
    Write-Host 'SYNTHETIC PS SQLCMD PASSWORD/PREFLIGHT MOCK: OK'
}
finally {
    foreach ($name in $envNames) {
        [Environment]::SetEnvironmentVariable($name,$original[$name],'Process')
    }
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item Function:\docker -ErrorAction SilentlyContinue
    Remove-Item Function:\dotnet -ErrorAction SilentlyContinue
    Remove-Variable MockSecret,MockFailAt,MockTrace -Scope Global -ErrorAction SilentlyContinue
}
