# Parse and invoke only the real SQL routines; never execute local-db's entrypoint.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'local-db.ps1'
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if ($errors.Count -gt 0) { throw 'Invalid PowerShell in local-db.ps1.' }
$previousPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')
$global:DbMockPassword = 'Synthetic_Db_Mock_2026!Only'
$global:DbMockCalls = 0
$global:DbMockFailure = $false
$global:DbMockThrow = $false
function global:docker {
    $argv = @($args | ForEach-Object { [string]$_ })
    $global:DbMockCalls++
    $index = [array]::IndexOf($argv,'-e')
    if ($argv -notcontains 'exec' -or $index -lt 0 -or $index + 1 -ge $argv.Count -or
        $argv[$index + 1] -cne 'SQLCMDPASSWORD') {
        throw 'Expected docker exec with the environment variable name, not its value.'
    }
    foreach ($arg in $argv) {
        if ($arg.StartsWith('SQLCMDPASSWORD=') -or $arg.Contains($global:DbMockPassword)) {
            throw 'SQL password leaked through Docker arguments.'
        }
    }
    if ($env:SQLCMDPASSWORD -cne $global:DbMockPassword) { throw 'Docker did not inherit the password.' }
    if ($global:DbMockThrow) { throw 'Synthetic native invocation failure.' }
    $global:LASTEXITCODE = if ($global:DbMockFailure) { 29 } else { 0 }
    if (-not $global:DbMockFailure -and $argv -contains '-W') { return '1' }
}
try {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $EnvFile = Join-Path $Root 'mock-not-read.env'
    $password = $global:DbMockPassword
    $db = 'JornadaSyntheticDev'
    $definitions = @($ast.EndBlock.Statements | Where-Object {
        $_ -is [System.Management.Automation.Language.FunctionDefinitionAst]
    })
    foreach ($name in @('Invoke-SqlCmd','Invoke-SqlScalar')) {
        $found = @($definitions | Where-Object { $_.Name -ceq $name })
        if ($found.Count -ne 1) { throw "Missing or duplicate SQL routine $name." }
        . ([scriptblock]::Create($found[0].Extent.Text))
    }
    $env:SQLCMDPASSWORD = 'PARENT_SCOPE_SENTINEL'
    foreach ($name in @('Invoke-SqlCmd','Invoke-SqlScalar')) {
        foreach ($case in @('success','nonzero','exception')) {
            $global:DbMockFailure = $case -eq 'nonzero'
            $global:DbMockThrow = $case -eq 'exception'
            $before = $global:DbMockCalls
            $rejected = $false
            try {
                if ($name -eq 'Invoke-SqlCmd') {
                    Invoke-SqlCmd -SqlCmdArgs @('-d',$db,'-Q','SELECT 1')
                } else {
                    $result = Invoke-SqlScalar -Query 'SELECT 1'
                    if ($case -eq 'success' -and $result -ne '1') { throw 'Unexpected scalar result.' }
                }
            } catch {
                if ($case -eq 'success') { throw }
                if ($case -eq 'nonzero' -and $_.Exception.Message -notlike '*sqlcmd*falhou*') { throw }
                if ($case -eq 'exception' -and $_.Exception.Message -notlike '*Synthetic native invocation failure*') { throw }
                $rejected = $true
            }
            if ($case -ne 'success' -and -not $rejected) { throw "$name did not reject $case." }
            if ($global:DbMockCalls -ne $before + 1) { throw "$name must call Docker once." }
            if ($env:SQLCMDPASSWORD -cne 'PARENT_SCOPE_SENTINEL') {
                throw "$name failed to restore caller environment after $case."
            }
        }
    }
    Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
    $global:DbMockFailure = $false
    $global:DbMockThrow = $false
    Invoke-SqlCmd -SqlCmdArgs @('-d',$db,'-Q','SELECT 1')
    if ($null -ne [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')) {
        throw 'The initially absent environment variable was not removed.'
    }
    if ($global:DbMockCalls -ne 7) { throw "Expected 7 Docker mock calls, got $global:DbMockCalls." }
    $global:LASTEXITCODE = 0
    Write-Host 'LOCAL-DB SQLCMD SECRET TRANSPORT/RESTORE MOCK: OK'
}
finally {
    if ($null -eq $previousPassword) {
        Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
    } else {
        $env:SQLCMDPASSWORD = $previousPassword
    }
    Remove-Item Function:\docker -ErrorAction SilentlyContinue
    Remove-Variable DbMockPassword,DbMockCalls,DbMockFailure,DbMockThrow -Scope Global -ErrorAction SilentlyContinue
}
