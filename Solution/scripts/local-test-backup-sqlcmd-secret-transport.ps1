# Only the real SQL helper functions are parsed/invoked; backup and restore never run.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot 'local-backup-restore-drill.ps1'
$tokens=$null; $errors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if($errors.Count -gt 0){throw 'Backup/restore PowerShell script has parse errors.'}
$previousSqlcmdPassword=[Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')
$global:DrillMockSecret='Synthetic_Backup_Password_2026!Only'
$global:DrillMockCalls=0
$global:DrillMockMode='success'
function global:docker {
    $argv=@($args|ForEach-Object{[string]$_})
    $global:DrillMockCalls++
    $index=[array]::IndexOf($argv,'-e')
    if($argv -notcontains 'exec' -or $index -lt 0 -or $index+1 -ge $argv.Count -or
       $argv[$index+1] -cne 'SQLCMDPASSWORD'){
        throw 'Backup/restore SQL must pass only -e SQLCMDPASSWORD.'
    }
    foreach($arg in $argv){
        if($arg.StartsWith('SQLCMDPASSWORD=') -or $arg.Contains($global:DrillMockSecret)){
            throw 'SQL secret found in the Docker process argv.'
        }
    }
    if($env:SQLCMDPASSWORD -cne $global:DrillMockSecret){
        throw 'Docker did not inherit the backup/restore SQL secret.'
    }
    if($global:DrillMockMode -eq 'throw'){throw 'Synthetic backup Docker exception.'}
    $global:LASTEXITCODE=if($global:DrillMockMode -eq 'exit'){29}else{0}
    if($global:DrillMockMode -eq 'success' -and $argv -contains '-W'){
        return ' 77 '
    }
}
try {
    $Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $sqlPassword=$global:DrillMockSecret
    $definitions=@($ast.EndBlock.Statements|Where-Object{
        $_ -is [System.Management.Automation.Language.FunctionDefinitionAst]
    })
    foreach($name in @('SqlCmd','Scalar')){
        $matched=@($definitions|Where-Object{$_.Name -ceq $name})
        if($matched.Count -ne 1){throw "Missing or duplicate backup SQL function $name."}
        . ([scriptblock]::Create($matched[0].Extent.Text))
    }
    $env:SQLCMDPASSWORD='PARENT_SCOPE_SENTINEL'
    foreach($name in @('SqlCmd','Scalar')){
        foreach($scenario in @('success','exit','throw')){
            $global:DrillMockMode=$scenario
            $before=$global:DrillMockCalls
            $rejected=$false
            try {
                if($name -eq 'SqlCmd'){
                    SqlCmd -SqlCmdArgs @('-d','JornadaRestoreMock','-Q','SELECT 1')
                } else {
                    $result=Scalar -Database 'JornadaRestoreMock' -Query 'SELECT 1'
                    if($scenario -eq 'success' -and $result -ne '77'){
                        throw 'Backup scalar result was not preserved.'
                    }
                }
            } catch {
                if($scenario -eq 'success'){throw}
                if($scenario -eq 'exit' -and $_.Exception.Message -notlike '*sqlcmd falhou*'){throw}
                if($scenario -eq 'throw' -and $_.Exception.Message -notlike '*Synthetic backup Docker exception*'){throw}
                $rejected=$true
            }
            if($scenario -ne 'success' -and -not $rejected){
                throw "$name swallowed the $scenario failure."
            }
            if($global:DrillMockCalls -ne $before+1){
                throw "$name did not call Docker exactly once."
            }
            if($env:SQLCMDPASSWORD -cne 'PARENT_SCOPE_SENTINEL'){
                throw "$name did not restore SQLCMDPASSWORD after $scenario."
            }
        }
    }
    Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
    $global:DrillMockMode='success'
    SqlCmd -SqlCmdArgs @('-d','JornadaRestoreMock','-Q','SELECT 1')
    if($null -ne [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')){
        throw 'Originally absent SQLCMDPASSWORD was not removed.'
    }
    if($global:DrillMockCalls -ne 7){
        throw "Expected 7 Docker calls, got $global:DrillMockCalls."
    }
    $global:LASTEXITCODE=0
    Write-Host 'BACKUP POWERSHELL SQLCMD SECRET TRANSPORT MOCK: OK'
}
finally {
    if($null -eq $previousSqlcmdPassword){
        Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
    } else {
        $env:SQLCMDPASSWORD=$previousSqlcmdPassword
    }
    Remove-Item Function:\docker -ErrorAction SilentlyContinue
    Remove-Variable DrillMockSecret,DrillMockCalls,DrillMockMode -Scope Global -ErrorAction SilentlyContinue
}
