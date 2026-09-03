$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $PSScriptRoot 'local-db.ps1') -Action up
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$vars = @{}
Get-Content (Join-Path $Root '.env') | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
        $parts = $line.Split('=',2); $vars[$parts[0].Trim()] = $parts[1]
    }
}
$port = if ($vars['JORNADA_SQL_PORT']) { $vars['JORNADA_SQL_PORT'] } else { '14333' }
$db = if ($vars['JORNADA_SQL_DATABASE']) { $vars['JORNADA_SQL_DATABASE'] } else { 'JornadaLocal' }
$env:JORNADA_TEST_SQL_CONNECTION = "Server=localhost,$port;Database=$db;User Id=sa;Password=$($vars['JORNADA_SQL_SA_PASSWORD']);TrustServerCertificate=true;Encrypt=false"
Push-Location $Root
try {
    if (-not (Get-Command python -ErrorAction SilentlyContinue)) { throw 'Python 3 é necessário para o gate OpenAPI.' }
    python scripts/openapi-contract-gate.py
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    python scripts/technical-closure-gate.py
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & (Join-Path $PSScriptRoot 'local-sql-runtime-smoke.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet restore Jornada.sln
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet build Jornada.sln --configuration Release --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test tests/Jornada.Tests/Jornada.Tests.csproj --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj --configuration Release --no-build
    exit $LASTEXITCODE
} finally { Pop-Location }
