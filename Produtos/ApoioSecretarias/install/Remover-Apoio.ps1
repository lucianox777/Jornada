[CmdletBinding()]
param([string]$Destino = "$env:LOCALAPPDATA\Jornada\ApoioSecretarias")
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Apoio às Secretarias.lnk'
if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force }
# Remove APENAS binários conhecidos. Os arquivos de origem, perfis, ZIPs e relatórios
# ficam fora de $Destino e NUNCA são apagados automaticamente.
foreach ($file in @('ApoioSecretarias.exe','ApoioSecretariasCLI.exe')) {
  $path = Join-Path $Destino $file
  if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}
Write-Host 'Executáveis removidos; dados e relatórios preservados.'
