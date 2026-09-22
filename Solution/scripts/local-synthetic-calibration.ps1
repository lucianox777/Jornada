param(
    [int]$People = 200000,
    [UInt64]$Seed = 42,
    [string]$ExpectedSeeds = '',
    [string]$RunGroupId = '',
    [ValidateSet('clean','independent','correlated','field')]
    [string]$ErrorProfile = 'correlated',
    [string]$DataReferencia = '2026-09-21T00:00:00-03:00',
    [string]$ApiBase = 'http://127.0.0.1:5098'
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = if ([string]::IsNullOrWhiteSpace($env:JORNADA_LOCAL_ENV_FILE)) {
    Join-Path $Root '.env'
}
else {
    [IO.Path]::GetFullPath($env:JORNADA_LOCAL_ENV_FILE)
}
$Example = Join-Path $Root '.env.example'

if ([string]::IsNullOrWhiteSpace($env:JORNADA_SYNTH_PSEUDONYMIZATION_KEY) -or
    [Text.Encoding]::UTF8.GetByteCount($env:JORNADA_SYNTH_PSEUDONYMIZATION_KEY) -lt 16) {
    throw 'Defina JORNADA_SYNTH_PSEUDONYMIZATION_KEY com ao menos 16 bytes.'
}

if (-not (Test-Path -LiteralPath $EnvFile -PathType Leaf)) {
    Copy-Item $Example $EnvFile
}

& (Join-Path $Root 'scripts/local-db.ps1') up -NoSyntheticCorpus
if ($LASTEXITCODE -ne 0) { throw "local-db reset falhou ($LASTEXITCODE)." }

$vars = @{}
Get-Content $EnvFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
        $parts = $line.Split('=', 2)
        $vars[$parts[0].Trim()] = $parts[1]
    }
}

$password = $vars['JORNADA_SQL_SA_PASSWORD']
$port = if ($vars['JORNADA_SQL_PORT']) { $vars['JORNADA_SQL_PORT'] } else { '14333' }
$db = if ($vars['JORNADA_SQL_DATABASE']) { $vars['JORNADA_SQL_DATABASE'] } else { 'JornadaLocal' }
if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD não definido.' }

Push-Location $Root
try {
    & docker compose --env-file $EnvFile exec -T -w /workspace -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -i database/Jornada_Dev_SyntheticCalibration_Cleanup.sql
    if ($LASTEXITCODE -ne 0) { throw "Limpeza sintética preservadora falhou ($LASTEXITCODE)." }
}
finally {
    Pop-Location
}

$env:ConnectionStrings__Jornada = "Server=localhost,$port;Database=$db;User Id=sa;Password=$password;TrustServerCertificate=true;Encrypt=false"
$env:Database__Provider = 'SqlServer'
$env:Ensaio__Mode = 'SYNTHETIC_CALIBRATION_DEV'
$env:Ensaio__Endpoints__IngestaoEntregas = "$($ApiBase.TrimEnd('/'))/api/v1/ingestao/entregas"
$env:Ensaio__SyntheticCalibration__People = [string]$People
$env:Ensaio__SyntheticCalibration__Seed = [string]$Seed
if ([string]::IsNullOrWhiteSpace($ExpectedSeeds)) {
    $env:Ensaio__SyntheticCalibration__ExpectedSeeds = [string]$Seed
}
else {
    $env:Ensaio__SyntheticCalibration__ExpectedSeeds = $ExpectedSeeds
}
if ([string]::IsNullOrWhiteSpace($RunGroupId)) {
    Remove-Item Env:Ensaio__SyntheticCalibration__RunGroupId -ErrorAction SilentlyContinue
}
else {
    [void][Guid]::Parse($RunGroupId)
    $env:Ensaio__SyntheticCalibration__RunGroupId = $RunGroupId
}
$env:Ensaio__SyntheticCalibration__ErrorProfile = $ErrorProfile
$env:Ensaio__SyntheticCalibration__DataReferencia = $DataReferencia

Push-Location $Root
try {
    & dotnet run --project src/Jornada.Ensaio --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Jornada.Ensaio falhou ($LASTEXITCODE)." }
}
finally {
    Pop-Location
}
