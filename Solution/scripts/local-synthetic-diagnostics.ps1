[CmdletBinding()]
param(
    [string]$EnvFile = '',
    [string]$DatabaseName = ''
)
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($EnvFile)) {
    $EnvFile = if ($env:JORNADA_LOCAL_ENV_FILE) { $env:JORNADA_LOCAL_ENV_FILE } else { Join-Path $Root '.env' }
}
$EnvFile = [IO.Path]::GetFullPath($EnvFile)
if (-not (Test-Path -LiteralPath $EnvFile -PathType Leaf)) {
    throw "Arquivo de ambiente não encontrado: $EnvFile"
}
$vars = @{}
Get-Content -LiteralPath $EnvFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
        $parts = $line.Split('=',2)
        $vars[$parts[0].Trim()] = $parts[1].Trim().Trim('"')
    }
}
$password = $vars['JORNADA_SQL_SA_PASSWORD']
if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD não definido.' }
$db = if ($DatabaseName) { $DatabaseName } elseif ($vars['JORNADA_SQL_DATABASE']) { $vars['JORNADA_SQL_DATABASE'] } else { 'JornadaLocal' }
if ($db -notmatch '^[A-Za-z0-9_]+$') { throw 'Nome de banco inválido.' }
Write-Host "Lendo diagnóstico agregado do banco $db (somente SELECT, sem limpeza)."
Push-Location $Root
try {
    & docker compose --env-file $EnvFile exec -T -w /workspace -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -w 900 -i database/Jornada_Dev_SyntheticCalibration_Diagnostics.sql
    if ($LASTEXITCODE -ne 0) { throw "Consulta de diagnóstico falhou ($LASTEXITCODE)." }
}
finally {
    Pop-Location
}
