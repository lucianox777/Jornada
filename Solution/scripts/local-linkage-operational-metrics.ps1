[CmdletBinding()]
param([string]$EnvFile = '', [string]$DatabaseName = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($EnvFile)) {
    $EnvFile = if ([string]::IsNullOrWhiteSpace($env:JORNADA_LOCAL_ENV_FILE)) {
        Join-Path $root '.env'
    } else { [IO.Path]::GetFullPath($env:JORNADA_LOCAL_ENV_FILE) }
}
$EnvFile = [IO.Path]::GetFullPath($EnvFile)
if (-not (Test-Path -LiteralPath $EnvFile -PathType Leaf)) { throw "Arquivo de ambiente ausente: $EnvFile" }
$values = @{}
Get-Content -LiteralPath $EnvFile | ForEach-Object {
    if ($_ -match '^\s*([^#=\s]+)\s*=(.*)$') {
        $values[$matches[1]] = $matches[2].Trim().Trim('"')
    }
}
$db = if (-not [string]::IsNullOrWhiteSpace($DatabaseName)) {
    $DatabaseName
} elseif ($values['JORNADA_SQL_DATABASE']) {
    $values['JORNADA_SQL_DATABASE']
} else { 'JornadaLocal' }
if ($db -notmatch '^[A-Za-z][A-Za-z0-9_]{0,100}$') { throw 'Nome de banco invalido.' }
$password = $values['JORNADA_SQL_SA_PASSWORD']
if ([string]::IsNullOrWhiteSpace($password)) { throw 'Senha SQL nao configurada.' }
Write-Host "Metricas agregadas de Linkage em ${db}: somente SELECT, sem limpeza/publicacao."
Push-Location $root
try {
    & docker compose --env-file $EnvFile exec -T -w /workspace -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -w 340 -i database/Jornada_Dev_LinkageOperationalMetrics.sql
    if ($LASTEXITCODE -ne 0) { throw "Consulta agregada falhou ($LASTEXITCODE)." }
}
finally { Pop-Location }
