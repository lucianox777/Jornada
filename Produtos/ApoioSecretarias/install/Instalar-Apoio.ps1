[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$Dist,
  [string]$Destino = "$env:LOCALAPPDATA\Jornada\ApoioSecretarias"
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$source = (Resolve-Path -LiteralPath $Dist).Path
foreach ($name in @('ApoioSecretarias.exe','ApoioSecretariasCLI.exe')) {
  if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) {
    throw "Distribuição incompleta: $name"
  }
}
New-Item -ItemType Directory -Force -Path $Destino | Out-Null
foreach ($name in @('ApoioSecretarias.exe','ApoioSecretariasCLI.exe')) {
  Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $Destino $name) -Force
}
# Não incluir segredos no instalador e não inspecionar dados da Secretaria.
$wsh = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Apoio às Secretarias.lnk'
$shortcut = $wsh.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $Destino 'ApoioSecretarias.exe'
$shortcut.WorkingDirectory = $Destino
$shortcut.Save()
Write-Host "Apoio instalado: $Destino"
Write-Host "Atalho: $shortcutPath"
