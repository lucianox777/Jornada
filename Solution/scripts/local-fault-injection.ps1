$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $PSScriptRoot 'local-db.ps1') -Action up
$vars = @{}
Get-Content (Join-Path $Root '.env') | ForEach-Object {
    $line=$_.Trim(); if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) { $p=$line.Split('=',2); $vars[$p[0].Trim()]=$p[1] }
}
$port = if ($vars['JORNADA_SQL_PORT']) { $vars['JORNADA_SQL_PORT'] } else { '14333' }
$db = if ($vars['JORNADA_SQL_DATABASE']) { $vars['JORNADA_SQL_DATABASE'] } else { 'JornadaLocal' }
$env:JORNADA_TEST_SQL_CONNECTION = "Server=localhost,$port;Database=$db;User Id=sa;Password=$($vars['JORNADA_SQL_SA_PASSWORD']);TrustServerCertificate=true;Encrypt=false"
$results = Join-Path $Root '.local/test-results'; New-Item -ItemType Directory -Force -Path $results | Out-Null
Push-Location $Root
try {
    if ($env:JORNADA_LOCKED_RESTORE -eq 'true') { dotnet restore Jornada.sln --locked-mode } else { dotnet restore Jornada.sln }; if ($LASTEXITCODE -ne 0) { throw 'dotnet restore falhou.' }
    dotnet build Jornada.sln --configuration Release --no-restore -warnaserror; if ($LASTEXITCODE -ne 0) { throw 'dotnet build falhou.' }
    dotnet test tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj --configuration Release --no-build --filter 'TestCategory=FaultInjection' --logger "trx;LogFileName=$results/fault-injection.trx"
    if ($LASTEXITCODE -ne 0) { throw 'Fault injection falhou.' }
    python3 scripts/test-evidence-gate.py .local/test-results/fault-injection.trx --forbid-skipped --minimum-tests 2 --summary .local/test-results/fault-injection-summary.json
    if ($LASTEXITCODE -ne 0) { throw 'Fault injection contém skip/falha ou evidência inválida.' }
} finally { Pop-Location }
Write-Host 'Fault injection concluído sem skips. Evidências: .local/test-results/fault-injection.trx e fault-injection-summary.json'
