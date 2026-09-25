# No Docker/SQL or credential files required: exercise the actual IBGE SQL functions via AST.
# The repair script's SQL routine is mocked and cannot write to a database.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$priorPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
$global:IbgeMockPassword = 'Synthetic_Ibge_Mock!2026'
$global:IbgeMockCalls = 0
$global:IbgeMockFailure = $false

function global:docker {
    $argv = @($args | ForEach-Object { [string]$_ })
    $global:IbgeMockCalls++
    $index = [array]::IndexOf($argv, '-e')
    if ($argv -notcontains 'exec' -or $index -lt 0 -or $index + 1 -ge $argv.Count -or
        $argv[$index + 1] -cne 'SQLCMDPASSWORD') {
        throw 'Docker must receive only the SQLCMDPASSWORD variable name.'
    }
    foreach ($argument in $argv) {
        if ($argument.Contains($global:IbgeMockPassword) -or $argument.StartsWith('SQLCMDPASSWORD=')) {
            throw 'SQL credential leaked to docker argv.'
        }
    }
    if ($env:SQLCMDPASSWORD -cne $global:IbgeMockPassword) {
        throw 'Docker did not inherit SQLCMDPASSWORD.'
    }
    $global:LASTEXITCODE = if ($global:IbgeMockFailure) { 23 } else { 0 }
    if (-not $global:IbgeMockFailure -and $argv -contains '-W') {
        return '1'
    }
}

try {
    $env:SQLCMDPASSWORD = 'PARENT_SCOPE_SENTINEL'
    $Root = $root
    $EnvFile = Join-Path $root 'mock-not-read.env'
    $password = $global:IbgeMockPassword
    $cases = @(
        @{ File='local-check-ibge-reference.ps1'; Functions=@('Test-SqlReady','Invoke-SqlScalar') },
        @{ File='local-diagnose-ibge-reference.ps1'; Functions=@('Test-SqlReady','Invoke-SqlScalar','Invoke-Sql') },
        @{ File='local-repair-ibge-reference.ps1'; Functions=@('Test-SqlReady','Invoke-SqlScalar','Invoke-SqlNonQuery') }
    )
    foreach ($case in $cases) {
        $file = Join-Path $PSScriptRoot $case.File
        $tokens = $null
        $parseErrors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors.Count) { throw "Invalid PowerShell in $($case.File)." }
        $topLevel = @($ast.EndBlock.Statements | Where-Object {
            $_ -is [System.Management.Automation.Language.FunctionDefinitionAst]
        })
        foreach ($name in $case.Functions) {
            $definitions = @($topLevel | Where-Object { $_.Name -ceq $name })
            if ($definitions.Count -ne 1) { throw "$($case.File): missing/duplicated $name." }
            # Dot-source the real function definition, not an imitation of its Docker call.
            . ([scriptblock]::Create($definitions[0].Extent.Text))
            $global:IbgeMockFailure = $false
            $before = $global:IbgeMockCalls
            if ($name -eq 'Test-SqlReady') {
                if (-not (Test-SqlReady)) { throw "$($case.File): SQL readiness success failed." }
            } elseif ($name -eq 'Invoke-SqlScalar') {
                if ((Invoke-SqlScalar -Database master -Query 'SELECT 1') -ne '1') {
                    throw "$($case.File): scalar result differs."
                }
            } elseif ($name -eq 'Invoke-Sql') {
                Invoke-Sql -Database master -Query 'SELECT 1' | Out-Null
            } else {
                Invoke-SqlNonQuery -Database master -Query 'SELECT 1' | Out-Null
            }
            if ($global:IbgeMockCalls -ne $before + 1) {
                throw "$($case.File): $name did not call Docker precisely once."
            }
            if ($env:SQLCMDPASSWORD -cne 'PARENT_SCOPE_SENTINEL') {
                throw "$($case.File): $name failed to restore parent env after success."
            }
            $global:IbgeMockFailure = $true
            $before = $global:IbgeMockCalls
            if ($name -eq 'Test-SqlReady') {
                if (Test-SqlReady) { throw "$($case.File): expected readiness failure." }
            } else {
                $rejected = $false
                try {
                    if ($name -eq 'Invoke-SqlScalar') {
                        Invoke-SqlScalar -Database master -Query 'SELECT 1' | Out-Null
                    } elseif ($name -eq 'Invoke-Sql') {
                        Invoke-Sql -Database master -Query 'SELECT 1' | Out-Null
                    } else {
                        Invoke-SqlNonQuery -Database master -Query 'SELECT 1' | Out-Null
                    }
                } catch {
                    if ($_.Exception.Message -notmatch 'sqlcmd falhou') { throw }
                    $rejected = $true
                }
                if (-not $rejected) { throw "$($case.File): $name failed to reject SQL errors." }
            }
            if ($global:IbgeMockCalls -ne $before + 1 -or
                $env:SQLCMDPASSWORD -cne 'PARENT_SCOPE_SENTINEL') {
                throw "$($case.File): $name did not restore after failure."
            }
        }
    }
    if ($global:IbgeMockCalls -ne 16) {
        throw "Expected 16 mock SQL commands, got $global:IbgeMockCalls."
    }
    # The last synthetic failure leaves LASTEXITCODE=23 even though all assertions passed.
    # Reset only on success; exceptions above still fail the job.
    $global:LASTEXITCODE = 0
    Write-Host 'IBGE SQLCMD PASSWORD TRANSPORT/RESTORE MOCK: OK'
}
finally {
    if ($null -eq $priorPassword) {
        Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
    } else {
        $env:SQLCMDPASSWORD = $priorPassword
    }
    Remove-Item Function:\docker -ErrorAction SilentlyContinue
    Remove-Variable IbgeMockPassword,IbgeMockCalls,IbgeMockFailure -Scope Global -ErrorAction SilentlyContinue
}
