# Exercise the actual release SQL image probe with a fully mocked Docker function.
# No Docker engine, SQL Server, volume, or release test suite is invoked.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot 'local-validate-release.ps1'
$tokens=$null; $errors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if($errors.Count -gt 0){throw 'Release validation source has PowerShell parse errors.'}
$priorSa=[Environment]::GetEnvironmentVariable('MSSQL_SA_PASSWORD','Process')
$priorSqlcmd=[Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')
$global:ProbeMockMode='success'
$global:ProbeMockRuns=0
$global:ProbeMockExecs=0
$global:ProbeMockRemoves=0
$global:ProbeMockInspects=0

function global:docker {
    $argv=@($args|ForEach-Object{[string]$_})
    if($argv.Count -lt 1){throw 'Missing Docker operation.'}
    foreach($arg in $argv){
        if($arg.StartsWith('MSSQL_SA_PASSWORD=') -or $arg.StartsWith('SQLCMDPASSWORD=')){
            throw 'Release probe included SQL password in Docker process argv.'
        }
        if($env:MSSQL_SA_PASSWORD -match '^Jd![0-9a-f]{32}9aA$' -and
           $arg.Contains($env:MSSQL_SA_PASSWORD)){
            throw 'Release probe embedded its generated secret in Docker argv.'
        }
    }
    switch($argv[0]){
        'rm' {
            if($argv -notcontains '-f' -or $argv -notcontains 'jornada-sql-integrity-probe'){
                throw 'Probe cleanup lost its expected container name.'
            }
            $global:ProbeMockRemoves++
            if($global:ProbeMockMode -eq 'cleanup-throw' -and $global:ProbeMockRemoves -eq 2){
                throw 'Synthetic probe cleanup exception.'
            }
            $global:LASTEXITCODE=0
        }
        'run' {
            $global:ProbeMockRuns++
            $password=$env:MSSQL_SA_PASSWORD
            if($password -notmatch '^Jd![0-9a-f]{32}9aA$' -or $env:SQLCMDPASSWORD -cne $password){
                throw 'Release probe did not export the same temporary SQL credentials.'
            }
            $present=$false
            for($i=0;$i -lt $argv.Count-1;$i++){
                if($argv[$i] -eq '-e' -and $argv[$i+1] -ceq 'MSSQL_SA_PASSWORD'){
                    $present=$true
                }
            }
            if(-not $present){throw 'Docker run must inherit MSSQL_SA_PASSWORD by name.'}
            if($global:ProbeMockMode -eq 'run-throw'){throw 'Synthetic probe Docker run exception.'}
            $global:LASTEXITCODE=0
            return 'synthetic-probe-container-id'
        }
        'exec' {
            $global:ProbeMockExecs++
            $password=$env:SQLCMDPASSWORD
            if($password -notmatch '^Jd![0-9a-f]{32}9aA$' -or $env:MSSQL_SA_PASSWORD -cne $password){
                throw 'Probe Docker exec did not inherit the same temporary SQL password.'
            }
            $present=$false
            for($i=0;$i -lt $argv.Count-1;$i++){
                if($argv[$i] -eq '-e' -and $argv[$i+1] -ceq 'SQLCMDPASSWORD'){
                    $present=$true
                }
            }
            if(-not $present){throw 'Docker exec must inherit SQLCMDPASSWORD by name.'}
            if($global:ProbeMockMode -eq 'probe-exit'){
                $global:LASTEXITCODE=29
                return
            }
            if($global:ProbeMockMode -eq 'version-throw' -and $argv -match 'SERVERPROPERTY'){
                throw 'Synthetic probe version SQL exception.'
            }
            $global:LASTEXITCODE=0
            if($argv -match 'SERVERPROPERTY'){return '16.0.555.1'}
        }
        'inspect' {
            $global:ProbeMockInspects++
            $global:LASTEXITCODE=0
            return 'exited'
        }
        default {throw "Unexpected probe Docker operation: $($argv[0])"}
    }
}
try {
    $definitions=@($ast.EndBlock.Statements|Where-Object{
        $_ -is [System.Management.Automation.Language.FunctionDefinitionAst]
    })
    foreach($name in @('Remove-SqlProbeContainer','Test-SqlServerImageRuntime')){
        $matched=@($definitions|Where-Object{$_.Name -ceq $name})
        if($matched.Count -ne 1){throw "Missing actual release probe function $name."}
        . ([scriptblock]::Create($matched[0].Extent.Text))
    }
    $image='mcr.microsoft.com/mssql/server@sha256:mock-only-never-pulled'
    foreach($mode in @('success','probe-exit','run-throw','version-throw','cleanup-throw')){
        $env:MSSQL_SA_PASSWORD='PARENT_SA_SENTINEL'
        $env:SQLCMDPASSWORD='PARENT_CMD_SENTINEL'
        $global:ProbeMockMode=$mode
        $global:ProbeMockRuns=0
        $global:ProbeMockExecs=0
        $global:ProbeMockRemoves=0
        $global:ProbeMockInspects=0
        $caught=$false
        try {
            $result=Test-SqlServerImageRuntime -Image $image
            if($mode -eq 'success' -and $result -ne $true){
                throw 'Release probe did not return success.'
            }
            if($mode -eq 'probe-exit' -and $result -ne $false){
                throw 'Release probe accepted failed SQL readiness.'
            }
        } catch {
            if($mode -eq 'success' -or $mode -eq 'probe-exit'){throw}
            if($mode -eq 'run-throw' -and $_.Exception.Message -notlike '*Synthetic probe Docker run exception*'){throw}
            if($mode -eq 'version-throw' -and $_.Exception.Message -notlike '*Synthetic probe version SQL exception*'){throw}
            if($mode -eq 'cleanup-throw' -and $_.Exception.Message -notlike '*Synthetic probe cleanup exception*'){throw}
            $caught=$true
        }
        if($mode -notin @('success','probe-exit') -and -not $caught){
            throw "$mode did not propagate the mocked Docker failure."
        }
        if($global:ProbeMockRuns -ne 1 -or $global:ProbeMockRemoves -ne 2){
            throw "$mode did not create and clean up the isolated probe exactly once."
        }
        if($mode -eq 'success' -and $global:ProbeMockExecs -ne 2){
            throw 'Successful release probe must execute both SQL checks.'
        }
        if($mode -eq 'probe-exit' -and
           ($global:ProbeMockExecs -ne 1 -or $global:ProbeMockInspects -ne 1)){
            throw 'Failed readiness must inspect a stopped probe once.'
        }
        if($env:MSSQL_SA_PASSWORD -cne 'PARENT_SA_SENTINEL' -or
           $env:SQLCMDPASSWORD -cne 'PARENT_CMD_SENTINEL'){
            throw "$mode did not restore the original caller environment."
        }
    }
    Remove-Item Env:\MSSQL_SA_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
    $global:ProbeMockMode='success'
    $result=Test-SqlServerImageRuntime -Image $image
    if($result -ne $true -or
       $null -ne [Environment]::GetEnvironmentVariable('MSSQL_SA_PASSWORD','Process') -or
       $null -ne [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')){
        throw 'Originally absent SQL credentials must remain absent.'
    }
    $global:LASTEXITCODE=0
    Write-Host 'RELEASE PROBE POWERSHELL SQL SECRET TRANSPORT MOCK: OK'
}
finally {
    if($null -eq $priorSa){Remove-Item Env:\MSSQL_SA_PASSWORD -ErrorAction SilentlyContinue}
    else{$env:MSSQL_SA_PASSWORD=$priorSa}
    if($null -eq $priorSqlcmd){Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue}
    else{$env:SQLCMDPASSWORD=$priorSqlcmd}
    Remove-Item Function:\docker -ErrorAction SilentlyContinue
    Remove-Variable ProbeMockMode,ProbeMockRuns,ProbeMockExecs,ProbeMockRemoves,ProbeMockInspects -Scope Global -ErrorAction SilentlyContinue
}
