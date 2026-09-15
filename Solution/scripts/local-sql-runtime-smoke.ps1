$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
if (-not (Test-Path $EnvFile)) { Copy-Item (Join-Path $Root '.env.example') $EnvFile }

$vars = @{}
Get-Content $EnvFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
        $parts = $line.Split('=',2)
        $vars[$parts[0].Trim()] = $parts[1]
    }
}

$password = $vars['JORNADA_SQL_SA_PASSWORD']
if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD não definido.' }
$db = if ($vars['JORNADA_SQL_DATABASE']) { $vars['JORNADA_SQL_DATABASE'] } else { 'JornadaLocal' }
if ($db -notmatch '^[A-Za-z0-9_]+$') { throw 'Nome de banco inválido.' }

Push-Location $Root
try {
    $cid = (& docker compose --env-file $EnvFile ps -q sqlserver | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw "docker compose ps falhou ($LASTEXITCODE)." }
    if ([string]::IsNullOrWhiteSpace($cid)) { throw 'SQL Server local não está em execução.' }

    # local-db.ps1 é a autoridade de bootstrap e já promove JornadaLocal ao schema 3.70.
    # Este smoke valida somente a semântica do runtime sobre essa base canônica; não deve
    # reaplicar Jornada_Fase1.sql (baseline 3.69), pois isso rebaixaria o marcador de schema.
    & docker compose --env-file $EnvFile exec -T -w /workspace -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -i database/Jornada_Runtime_Smoke.sql
    if ($LASTEXITCODE -ne 0) { throw "Runtime SQL smoke falhou ($LASTEXITCODE)." }

    Write-Host 'LOCAL SQL RUNTIME SMOKE 3.70: OK'
}
finally {
    Pop-Location
}
