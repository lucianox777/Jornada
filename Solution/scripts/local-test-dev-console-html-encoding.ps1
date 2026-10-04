$ErrorActionPreference='Stop'
$targets=@('scripts/dev-console-initial-config.ps1','scripts/dev-console-contract-bundle.ps1')
Set-Alias -Name h -Value Get-History -Scope Local
foreach($target in $targets){
  $source=Get-Content -Raw -Encoding UTF8 $target
  $null=[scriptblock]::Create($source)
  if($source -match '(?im)^\s*function\s+H\s*\(' -or $source -match '\$\(\s*H\s+'){
    throw "$target reintroduziu helper H, que colide com o alias h=Get-History no Windows PowerShell 5.1."
  }
  if($source -notmatch 'function\s+ConvertTo-HtmlEncodedText'){
    throw "$target perdeu o encoder HTML explícito."
  }
}
$guid='4f0fef68-8777-4ad0-a70d-52fb7e21dcf0'
$encoded=[System.Net.WebUtility]::HtmlEncode($guid)
if($encoded -ne $guid){ throw 'Encoding do GUID alterou valor inesperadamente.' }
Write-Host 'DEV CONSOLE HTML ENCODER/ALIAS REGRESSION: OK'
