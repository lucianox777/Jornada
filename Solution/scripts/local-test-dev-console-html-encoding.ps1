$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'dev-console-html.ps1')

$guid='57E6D8EB-42B0-49CB-8EC8-206A377B0BC5'
$encodedGuid=ConvertTo-DevConsoleHtmlText $guid
if($encodedGuid -ne $guid){
    throw "GUID alterado indevidamente pelo encoder HTML: $encodedGuid"
}

$encodedSpecial=ConvertTo-DevConsoleHtmlText '<>&"'
if($encodedSpecial -ne '&lt;&gt;&amp;&quot;'){
    throw "Encoding HTML inesperado: $encodedSpecial"
}

$aliasH=Get-Alias -Name h -ErrorAction SilentlyContinue
if($null -ne $aliasH -and $aliasH.Definition -ne 'Get-History'){
    throw "Alias h inesperado no ambiente de teste: $($aliasH.Definition)"
}

if((Get-Command ConvertTo-DevConsoleHtmlText).CommandType -ne 'Function'){
    throw 'ConvertTo-DevConsoleHtmlText não foi carregada como função.'
}

Write-Host 'DEV CONSOLE HTML ENCODER / ALIAS h: OK'
