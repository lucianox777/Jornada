$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
if (-not (Test-Path $EnvFile)) { Copy-Item (Join-Path $Root '.env.example') $EnvFile }
$vars = @{}
Get-Content $EnvFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
        $parts = $line.Split('=',2); $vars[$parts[0].Trim()] = $parts[1]
    }
}
$password = $vars['JORNADA_SQL_SA_PASSWORD']
if (-not $password) { throw 'JORNADA_SQL_SA_PASSWORD não definido.' }
$db = if ($vars['JORNADA_SQL_DATABASE']) { $vars['JORNADA_SQL_DATABASE'] } else { 'JornadaLocal' }
if ($db -notmatch '^[A-Za-z0-9_]+$') { throw 'Nome de banco inválido.' }
Push-Location $Root
try {
    $cid = (docker compose --env-file $EnvFile ps -q sqlserver).Trim()
    if (-not $cid) { throw 'SQL Server local não está em execução.' }
    foreach ($file in @('database/Jornada_Fase1.sql','database/Jornada_Seed_Dev.sql','database/Jornada_Runtime_Smoke.sql')) {
        Get-Content -Raw $file | docker exec -i -e "SQLCMDPASSWORD=$password" $cid /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $db -i /dev/stdin
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
} finally { Pop-Location }
