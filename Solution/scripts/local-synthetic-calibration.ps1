param(
    [int]$People = 200000,
    [UInt64]$Seed = 42,
    [string]$ExpectedSeeds = '',
    [string]$RunGroupId = '',
    [ValidateSet('clean','independent','correlated','field')]
    [string]$ErrorProfile = 'correlated',
    [string]$DataReferencia = '2026-09-21T00:00:00-03:00',
    [string]$ApiBase = 'http://127.0.0.1:5098',
    [ValidateRange(0,12)]
    [int]$Waves = 0,
    [string]$StratifiedErrorsConfig = '',
    [string]$BrazilianNameErrorsConfig = '',
    [switch]$AllowSharedDatabase
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
    if (-not [string]::IsNullOrWhiteSpace($env:JORNADA_LOCAL_ENV_FILE)) { throw "JORNADA_LOCAL_ENV_FILE aponta para arquivo inexistente: $EnvFile" }
    if (-not (Get-Command python -ErrorAction SilentlyContinue)) { throw 'Python 3 é necessário para gerar a credencial local.' }
    & python (Join-Path $PSScriptRoot 'local_env_bootstrap.py') --check-docker-volume
    if ($LASTEXITCODE -ne 0) { throw 'Bootstrap seguro do .env falhou.' }
}

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

# A Bronze deste ensaio e temporaria e nao e compartilhada com NODE1/NODE2.
# A guarda deve preceder local-db up e qualquer limpeza, inclusive em DEV.
$canonical = Join-Path $Root '.env'
$originalDb = 'JornadaLocal'
if (Test-Path -LiteralPath $canonical -PathType Leaf) {
    foreach ($line in Get-Content -LiteralPath $canonical) {
        if ($line -match '^\s*JORNADA_SQL_DATABASE\s*=\s*(\S+)') {
            $originalDb = $matches[1].Trim().Trim('"')
        }
    }
}
if ([string]::Equals($db, $originalDb, [StringComparison]::OrdinalIgnoreCase) -and -not $AllowSharedDatabase) {
    throw "Ensaio sintetico recusado: $db e o banco original. Use .env.synthetic.local com JORNADA_SQL_DATABASE=JornadaSyntheticDev e JORNADA_LOCAL_ENV_FILE apontando para essa copia. -AllowSharedDatabase e excepcional."
}
if ($AllowSharedDatabase) {
    Write-Warning 'Banco original explicitamente autorizado; a guarda SQL de exclusividade continua obrigatoria.'
}

& (Join-Path $Root 'scripts/local-db.ps1') up -NoSyntheticCorpus -DatabaseName $db
if ($LASTEXITCODE -ne 0) { throw "local-db up falhou ($LASTEXITCODE)." }

Push-Location $Root
try {
    # Fail-closed antes de apagar dados operacionais: NODE1/NODE2 podem
    # consumir a entrega sem acesso à Bronze temporária do ensaio.
    & docker compose --env-file $EnvFile exec -T -w /workspace -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -i database/Jornada_Dev_SyntheticCalibration_ExclusivePreflight.sql
    if ($LASTEXITCODE -ne 0) { throw "Preflight recusou o banco DEV compartilhado; nenhum dado foi limpo ($LASTEXITCODE)." }

    & docker compose --env-file $EnvFile exec -T -w /workspace -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -i database/Jornada_Dev_SyntheticCalibration_Cleanup.sql
    if ($LASTEXITCODE -ne 0) { throw "Limpeza sintética preservadora falhou ($LASTEXITCODE)." }
}
finally {
    Pop-Location
}

$env:ConnectionStrings__Jornada = "Server=localhost,$port;Database=$db;User Id=sa;Password=$password;TrustServerCertificate=true;Encrypt=false"
$env:Database__Provider = 'SqlServer'
$env:Ensaio__Mode = 'SYNTHETIC_CALIBRATION_DEV'
if ($Waves -eq 1) { throw 'Waves deve ser 0 (carga única) ou entre 2 e 12.' }
if ($Waves -ge 2) {
    $env:Ensaio__Mode = 'SYNTHETIC_WAVES_DEV'
    $env:Ensaio__SyntheticCalibration__WaveCount = [string]$Waves
}
else {
    Remove-Item Env:Ensaio__SyntheticCalibration__WaveCount -ErrorAction SilentlyContinue
}
if (-not [string]::IsNullOrWhiteSpace($StratifiedErrorsConfig)) {
    $env:Ensaio__SyntheticCalibration__StratifiedErrorsConfig = [IO.Path]::GetFullPath($StratifiedErrorsConfig)
}
if (-not [string]::IsNullOrWhiteSpace($BrazilianNameErrorsConfig)) {
    $env:Ensaio__SyntheticCalibration__BrazilianNameErrorsConfig = [IO.Path]::GetFullPath($BrazilianNameErrorsConfig)
}
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
    if ($LASTEXITCODE -ne 0) {
        $ensaioExitCode = $LASTEXITCODE
        # O teste TEST e os limiares congelados NÃO são recalibrados aqui.
        # O diagnóstico SQL é leitura agregada e deve ocorrer ANTES de outra limpeza DEV.
        try { & (Join-Path $Root 'scripts/local-synthetic-diagnostics.ps1') -EnvFile $EnvFile -DatabaseName $db }
        catch { Write-Warning "Diagnóstico SQL indisponível: $($_.Exception.Message)" }
        throw "Jornada.Ensaio falhou ($ensaioExitCode); diagnóstico agregado acima, se disponível."
    }
}
finally {
    Pop-Location
}
