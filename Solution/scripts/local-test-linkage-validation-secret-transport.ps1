# Test only the real independent validation SQL functions; never run its entrypoint.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot 'local-linkage-validation.ps1'
$tokens=$null; $errors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Validation PowerShell script did not parse.'}
$prior=[Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')
$global:ValidationMockSecret='Synthetic_Validation_Secret_2026!'
$global:ValidationMockMode='success'
$global:ValidationMockCalls=0
function Write-CommandLine([string]$Executable,[string[]]$Arguments){
  if($Executable -ne 'docker' -or $Arguments -notcontains 'SQLCMDPASSWORD=<redacted>' -or
     $Arguments -contains $global:ValidationMockSecret){
    throw 'The command echo must redact SQL credentials.'
  }
}
function global:docker {
  $argv=@($args|ForEach-Object{[string]$_})
  $global:ValidationMockCalls++
  $idx=[array]::IndexOf($argv,'-e')
  if($argv -notcontains 'exec' -or $idx -lt 0 -or $idx+1 -ge $argv.Count -or
     $argv[$idx+1] -cne 'SQLCMDPASSWORD'){throw 'Expected -e SQLCMDPASSWORD.'}
  foreach($arg in $argv){
    if($arg.StartsWith('SQLCMDPASSWORD=') -or $arg.Contains($global:ValidationMockSecret)){
      throw 'SQL secret found in Docker argv.'
    }
  }
  if($env:SQLCMDPASSWORD -cne $global:ValidationMockSecret){throw 'Docker did not inherit SQLCMDPASSWORD.'}
  if($argv -contains '/workspace/scripts/local-progressive-identity-backfill.sql'){
    if($argv -notcontains '-v' -or $argv -notcontains 'PAGE_SIZE=1000'){
      throw 'Progressive backfill lost PAGE_SIZE=1000.'
    }
  }
  if($global:ValidationMockMode -eq 'throw'){throw 'Synthetic Docker exception.'}
  $global:LASTEXITCODE=if($global:ValidationMockMode -eq 'exit'){29}else{0}
  if($global:ValidationMockMode -eq 'success' -and $argv -contains '-Q'){
    return @(' 17 ','(1 row affected)',' 18 ')
  }
}
try {
  $Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
  $EnvFile=Join-Path $Root 'test-only-never-open.env'
  $password=$global:ValidationMockSecret
  $db='JornadaMock'
  $defs=@($ast.EndBlock.Statements|Where-Object{
    $_ -is [System.Management.Automation.Language.FunctionDefinitionAst]
  })
  foreach($name in @('Invoke-SqlFile','Get-SqlLines','Get-SqlScalar')){
    $items=@($defs|Where-Object{$_.Name -ceq $name})
    if($items.Count -ne 1){throw "Missing real SQL function $name."}
    . ([scriptblock]::Create($items[0].Extent.Text))
  }
  $full=Get-Content -LiteralPath $source -Raw -Encoding UTF8
  if(-not $full.Contains("Invoke-SqlFile -ContainerPath '/workspace/scripts/local-progressive-identity-backfill.sql' -SqlCmdArgs @('-v','PAGE_SIZE=1000')")){
    throw 'Backfill must call the tested SQL helper.'
  }
  foreach($call in @('file','backfill','lines','scalar')){
    foreach($mode in @('success','exit','throw')){
      $env:SQLCMDPASSWORD='PARENT_SCOPE_SENTINEL'
      $global:ValidationMockMode=$mode
      $before=$global:ValidationMockCalls
      $rejected=$false
      try {
        switch($call){
          file {Invoke-SqlFile -ContainerPath '/workspace/test-fixture.sql'}
          backfill {Invoke-SqlFile -ContainerPath '/workspace/scripts/local-progressive-identity-backfill.sql' -SqlCmdArgs @('-v','PAGE_SIZE=1000')}
          lines {
            $lines=@(Get-SqlLines 'SELECT 17')
            if($mode -eq 'success' -and ($lines.Count -ne 2 -or $lines[0] -ne '17' -or $lines[1] -ne '18')){
              throw 'SQL line output changed.'
            }
          }
          scalar {
            $value=Get-SqlScalar 'SELECT 18'
            if($mode -eq 'success' -and $value -ne '18'){throw 'SQL scalar output changed.'}
          }
        }
      } catch {
        if($mode -eq 'success'){throw}
        if($mode -eq 'exit' -and $_.Exception.Message -notlike '*sqlcmd*falhou*'){throw}
        if($mode -eq 'throw' -and $_.Exception.Message -notlike '*Synthetic Docker exception*'){throw}
        $rejected=$true
      }
      if($mode -ne 'success' -and -not $rejected){throw "$call swallowed the $mode failure."}
      if($global:ValidationMockCalls -ne $before+1){throw "$call did not call Docker exactly once."}
      if($env:SQLCMDPASSWORD -cne 'PARENT_SCOPE_SENTINEL'){throw "$call failed to restore SQLCMDPASSWORD."}
    }
  }
  Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
  $global:ValidationMockMode='success'
  Invoke-SqlFile -ContainerPath '/workspace/test-fixture.sql'
  if($null -ne [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')){
    throw 'Missing original SQLCMDPASSWORD must remain absent.'
  }
  if($global:ValidationMockCalls -ne 13){throw 'Expected thirteen isolated Docker invocations.'}
  $global:LASTEXITCODE=0
  Write-Host 'LINKAGE VALIDATION PS SQLCMD SECRET TRANSPORT MOCK: OK'
}
finally {
  if($null -eq $prior){Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue}
  else {$env:SQLCMDPASSWORD=$prior}
  Remove-Item Function:\docker -ErrorAction SilentlyContinue
  Remove-Variable ValidationMockSecret,ValidationMockMode,ValidationMockCalls -Scope Global -ErrorAction SilentlyContinue
}
