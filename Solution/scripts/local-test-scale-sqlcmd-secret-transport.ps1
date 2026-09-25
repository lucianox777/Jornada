# Exercises only the real scale harness SQL functions via AST, never the reset entrypoint.
# Uses a fake Docker function on Windows PowerShell 5.1 and pwsh; no SQL connection.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$path=Join-Path $PSScriptRoot 'local-scale.ps1'
$tokens=$null; $errors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
if($errors.Count -gt 0){throw 'local-scale.ps1 has PowerShell parse errors.'}
$previousSqlcmdPassword=[Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')
$global:ScaleMockSecret='Synthetic_Scale_Password_2026!Only'
$global:ScaleMockCount=0
$global:ScaleMockFailure='none'

function global:docker {
    $argv=@($args|ForEach-Object{[string]$_})
    $global:ScaleMockCount++
    $index=[array]::IndexOf($argv,'-e')
    if($argv -notcontains 'exec' -or $index -lt 0 -or $index+1 -ge $argv.Count -or
       $argv[$index+1] -cne 'SQLCMDPASSWORD'){
        throw 'Scale SQL must use docker exec -e SQLCMDPASSWORD.'
    }
    foreach($arg in $argv){
        if($arg.StartsWith('SQLCMDPASSWORD=') -or $arg.Contains($global:ScaleMockSecret)){
            throw 'SQL password found in scale Docker process argv.'
        }
    }
    if($env:SQLCMDPASSWORD -cne $global:ScaleMockSecret){
        throw 'Docker did not inherit the scale SQL password.'
    }
    if($global:ScaleMockFailure -eq 'throw'){throw 'Synthetic Docker exception.'}
    if($global:ScaleMockFailure -eq 'exit'){
        $global:LASTEXITCODE=29
        return
    }
    $global:LASTEXITCODE=0
    if($argv -contains '-y'){return ' scalar-value '}
    if($argv -contains '-h'){return @(' first ','second ')}
}
try {
    $Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $EnvFile=Join-Path $Root 'mock-only-do-not-open.env'
    $sqlPassword=$global:ScaleMockSecret
    $db='JornadaScaleMock'
    $definitions=@($ast.EndBlock.Statements|Where-Object{
        $_ -is [System.Management.Automation.Language.FunctionDefinitionAst]
    })
    foreach($name in @('SqlCmd','Scalar','QueryLines')){
        $matched=@($definitions|Where-Object{$_.Name -ceq $name})
        if($matched.Count -ne 1){throw "Missing or duplicate real scale SQL function: $name."}
        . ([scriptblock]::Create($matched[0].Extent.Text))
    }
    $env:SQLCMDPASSWORD='PARENT_SCOPE_SENTINEL'
    foreach($name in @('SqlCmd','Scalar','QueryLines')){
        foreach($scenario in @('success','exit','throw')){
            $global:ScaleMockFailure=$scenario
            $before=$global:ScaleMockCount
            $rejected=$false
            try {
                switch($name){
                    'SqlCmd' {SqlCmd -SqlCmdArgs @('-d',$db,'-Q','SELECT 1')}
                    'Scalar' {
                        $value=Scalar -Query 'SELECT 1'
                        if($scenario -eq 'success' -and $value -ne 'scalar-value'){
                            throw 'Scale scalar output was not returned.'
                        }
                    }
                    'QueryLines' {
                        $lines=@(QueryLines -Query 'SELECT 1')
                        if($scenario -eq 'success' -and
                            ($lines.Count -ne 2 -or $lines[0] -ne 'first' -or $lines[1] -ne 'second')){
                            throw 'Scale query lines output was not returned.'
                        }
                    }
                }
            } catch {
                if($scenario -eq 'success'){throw}
                if($scenario -eq 'exit' -and $_.Exception.Message -notlike '*sqlcmd falhou*'){throw}
                if($scenario -eq 'throw' -and $_.Exception.Message -notlike '*Synthetic Docker exception*'){throw}
                $rejected=$true
            }
            if($scenario -ne 'success' -and -not $rejected){
                throw "$name did not reject the $scenario failure."
            }
            if($global:ScaleMockCount -ne $before+1){
                throw "$name did not execute exactly one SQL call."
            }
            if($env:SQLCMDPASSWORD -cne 'PARENT_SCOPE_SENTINEL'){
                throw "$name did not restore the caller SQLCMDPASSWORD after $scenario."
            }
        }
    }
    # Absent original password must remain absent after a successful command.
    Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
    $global:ScaleMockFailure='none'
    SqlCmd -SqlCmdArgs @('-d',$db,'-Q','SELECT 1')
    if($null -ne [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')){
        throw 'Initially absent SQLCMDPASSWORD must remain absent.'
    }
    if($global:ScaleMockCount -ne 10){
        throw "Expected 10 mocked Docker calls, got $global:ScaleMockCount."
    }
    $global:LASTEXITCODE=0
    Write-Host 'SCALE POWERSHELL SQLCMD SECRET TRANSPORT MOCK: OK'
}
finally {
    if($null -eq $previousSqlcmdPassword){
        Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
    } else {
        $env:SQLCMDPASSWORD=$previousSqlcmdPassword
    }
    Remove-Item Function:\docker -ErrorAction SilentlyContinue
    Remove-Variable ScaleMockSecret,ScaleMockCount,ScaleMockFailure -Scope Global -ErrorAction SilentlyContinue
}
