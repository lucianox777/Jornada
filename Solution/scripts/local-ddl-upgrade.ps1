[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
$Example = Join-Path $Root '.env.example'
$BaselineRel = if ($env:JORNADA_DDL_BASELINE) { $env:JORNADA_DDL_BASELINE } else { 'database/baselines/Jornada_Fase1_v3.65.sql' }
$BaselineSeedRel = if ($env:JORNADA_DDL_BASELINE_SEED) { $env:JORNADA_DDL_BASELINE_SEED } else { 'database/baselines/Jornada_Seed_Dev_v3.65.sql' }
$CurrentRel = if ($env:JORNADA_DDL_CURRENT) { $env:JORNADA_DDL_CURRENT } else { 'database/Jornada_Fase1_v3.70.sql' }
$Db = if ($env:JORNADA_DDL_UPGRADE_DATABASE) { $env:JORNADA_DDL_UPGRADE_DATABASE } else { 'JornadaDdlUpgradeCheck' }

foreach ($command in @('docker', 'dotnet')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Comando '$command' não encontrado no PATH."
    }
}
$Python = Get-Command python3 -ErrorAction SilentlyContinue
if ($null -eq $Python) { $Python = Get-Command python -ErrorAction SilentlyContinue }
if ($null -eq $Python) { throw 'Python 3 não encontrado no PATH.' }

if (-not (Test-Path -LiteralPath $EnvFile)) { Copy-Item $Example $EnvFile }
$vars = @{}
Get-Content $EnvFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
        $p = $line.Split('=', 2)
        $vars[$p[0].Trim()] = $p[1]
    }
}
$password = $vars['JORNADA_SQL_SA_PASSWORD']
if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD não definido.' }
$port = if ($vars['JORNADA_SQL_PORT']) { $vars['JORNADA_SQL_PORT'] } else { '14333' }
if ($port -notmatch '^\d+$') { throw 'JORNADA_SQL_PORT inválida.' }
if ($Db -notmatch '^[A-Za-z0-9_]+$') { throw 'Nome de banco inválido.' }
foreach ($relative in @($BaselineRel, $BaselineSeedRel, $CurrentRel)) {
    if (-not (Test-Path -LiteralPath (Join-Path $Root $relative))) { throw "Artefato DDL não encontrado: $relative" }
}

$activeSdk = (& dotnet --version | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or $activeSdk -ne '8.0.424') {
    throw "SDK ativo deve ser exatamente 8.0.424 (global.json); atual='$activeSdk'."
}

$OutDir = Join-Path $Root '.local/ddl-upgrade'
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Invoke-Compose {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    Push-Location $Root
    try {
        & docker compose --env-file $EnvFile @Arguments
        if ($LASTEXITCODE -ne 0) { throw "docker compose $($Arguments -join ' ') falhou." }
    }
    finally { Pop-Location }
}

function Invoke-Sql {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    Push-Location $Root
    try {
        $output = @(& docker compose --env-file $EnvFile exec -T -w /workspace -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I @Arguments)
        if ($LASTEXITCODE -ne 0) { throw 'sqlcmd falhou.' }
        return $output
    }
    finally { Pop-Location }
}

function Invoke-Scalar {
    param([Parameter(Mandatory = $true)][string]$Query)
    $lines = @(Invoke-Sql @('-d', $Db, '-W', '-h', '-1', '-y', '0', '-w', '65535', '-Q', "SET NOCOUNT ON; $Query")) |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ }
    if ($lines.Count -eq 0) { return '' }
    return $lines[-1]
}

function Wait-SqlHealthy {
    for ($i = 0; $i -lt 60; $i++) {
        $status = (& docker inspect -f '{{.State.Health.Status}}' jornada-sqlserver-local 2>$null | Out-String).Trim()
        if ($status -eq 'healthy') { return }
        Start-Sleep -Seconds 2
    }
    throw 'SQL Server não ficou healthy.'
}

function Get-Fingerprint {
    param([Parameter(Mandatory = $true)][string]$Tag)
    $path = Join-Path $OutDir "fingerprint-$Tag.txt"
    $lines = @(Invoke-Sql @('-d', $Db, '-i', 'database/Jornada_Dev_DdlFingerprint.sql', '-W', '-h', '-1')) |
        Where-Object { $_.Trim() }
    $lines | Set-Content -Encoding UTF8 $path
    return (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant()
}

function Assert-Sentinel {
    $count = (Invoke-Scalar "SELECT COUNT(*) FROM ref.gestor WHERE codigo='ZZ_UPGRADE_SENTINEL' AND nome='Sentinela DDL Upgrade';").Replace(' ', '')
    if ($count -ne '1') { throw 'Dado sentinela não foi preservado.' }
}

function Assert-PhoneV2 {
    $query = "SELECT CASE WHEN ref.fn_telefone_br_canonico_v2(N'00 55 11 99999-0001')='5511999990001' AND ref.fn_telefone_br_canonico_v2(N'+55 (11) 99999-0001')='5511999990001' AND ref.fn_telefone_br_canonico_v2(NCHAR(9)+N'+1 (212) 555-0100'+NCHAR(13)+NCHAR(10))='12125550100' AND ref.fn_telefone_br_canonico_v2(NCHAR(160)+N'+55 (11) 99999-0001'+NCHAR(160))='5511999990001' AND (SELECT atributo_instancia_chave FROM silver.pessoa_atributo_observacao WHERE source_record_id='SEH001-TEL-1')='5511999990001' AND (SELECT atributo_instancia_chave FROM gold.pessoa_atributo WHERE source_record_id='SEH001-TEL-1' AND vigencia_fim IS NULL)='5511999990001' THEN 1 ELSE 0 END;"
    if ((Invoke-Scalar $query).Replace(' ', '') -ne '1') { throw 'Migração TELEFONE_BR_CANONICO_V2 não convergiu a chave legada 00.' }
}

function Assert-EmailV2 {
    $query = "SELECT CASE WHEN ref.fn_email_canonico_v2(N'JOSÉ@EXAMPLE.ORG')=N'josÉ@example.org' AND ref.fn_email_canonico_v2(N'Jose'+NCHAR(769)+N'@Example.org')=N'jose'+NCHAR(769)+N'@example.org' AND (SELECT atributo_instancia_chave FROM silver.pessoa_atributo_observacao WHERE source_record_id='SEH002-EMAIL-1')=N'josÉ@example.org' AND (SELECT atributo_instancia_chave FROM gold.pessoa_atributo WHERE source_record_id='SEH002-EMAIL-1' AND vigencia_fim IS NULL)=N'josÉ@example.org' THEN 1 ELSE 0 END;"
    if ((Invoke-Scalar $query).Replace(' ', '') -ne '1') { throw 'Migração EMAIL_CANONICO_V2 não convergiu a chave legada.' }
}

function Assert-SchemaMarker {
    $query = "SELECT CASE WHEN CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.BaseNormativa'))=N'3.62' AND CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema'))=N'3.70' THEN 1 ELSE 0 END;"
    if ((Invoke-Scalar $query).Replace(' ', '') -ne '1') { throw 'Marcador de versão do schema não está em Base 3.62 / Solution 3.70.' }
}

function Write-InvariantSnapshot {
    param([Parameter(Mandatory = $true)][string]$Path)
    $lines = @(Invoke-Sql @('-d', $Db, '-i', 'database/Jornada_Upgrade_Invariants.sql', '-y', '0', '-w', '65535'))
    $started = $false
    $jsonLines = @()
    foreach ($line in $lines) {
        if (-not $started -and $line -match '^\s*\{') { $started = $true }
        if ($started) { $jsonLines += $line.TrimEnd("`r") }
    }
    if ($jsonLines.Count -eq 0) { throw 'Jornada_Upgrade_Invariants.sql não produziu JSON.' }
    [IO.File]::WriteAllText($Path, (($jsonLines -join '') + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
}

function Invoke-ProgressiveIdentityBackfill {
    Invoke-Sql @('-d', $Db, '-i', 'database/Jornada_Identidade_Progressiva.sql') | Out-Null

    $harness = Join-Path $OutDir 'identity-backfill'
    New-Item -ItemType Directory -Force $harness | Out-Null
    $project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../../src/Jornada.Processor.Worker/Jornada.Processor.Worker.csproj" />
    <ProjectReference Include="../../../src/Jornada.Operational.Sql/Jornada.Operational.Sql.csproj" />
    <Compile Include="../../../scripts/progressive-identity-backfill.cs" Link="Program.cs" />
  </ItemGroup>
</Project>
'@
    [IO.File]::WriteAllText((Join-Path $harness 'IdentityBackfill.csproj'), $project, [Text.UTF8Encoding]::new($false))

    Push-Location $Root
    try {
        & dotnet restore (Join-Path $harness 'IdentityBackfill.csproj') --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Restore do backfill progressivo falhou.' }
        & dotnet build (Join-Path $harness 'IdentityBackfill.csproj') -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Build do backfill progressivo falhou.' }

        $oldProvider = $env:JORNADA_PROGRESSIVE_PROVIDER
        $oldConnection = $env:JORNADA_PROGRESSIVE_CONNECTION
        $oldPageSize = $env:JORNADA_PROGRESSIVE_PAGE_SIZE
        try {
            $env:JORNADA_PROGRESSIVE_PROVIDER = 'SqlServer'
            $env:JORNADA_PROGRESSIVE_CONNECTION = "Server=localhost,$port;Database=$Db;User Id=sa;Password=$password;TrustServerCertificate=true;Encrypt=false"
            $env:JORNADA_PROGRESSIVE_PAGE_SIZE = '1000'
            $output = @(& dotnet run --project (Join-Path $harness 'IdentityBackfill.csproj') -c Release --no-build --no-restore 2>&1)
            $output | Tee-Object -FilePath (Join-Path $OutDir 'progressive-backfill.log') | Write-Host
            if ($LASTEXITCODE -ne 0) { throw 'Backfill progressivo falhou.' }
            if (-not (($output -join "`n").Contains('PROGRESSIVE IDENTITY BACKFILL: OK provider=SqlServer'))) {
                throw 'Backfill progressivo não confirmou conclusão canônica.'
            }
        }
        finally {
            $env:JORNADA_PROGRESSIVE_PROVIDER = $oldProvider
            $env:JORNADA_PROGRESSIVE_CONNECTION = $oldConnection
            $env:JORNADA_PROGRESSIVE_PAGE_SIZE = $oldPageSize
        }
    }
    finally { Pop-Location }

    $missing = (Invoke-Scalar "SELECT COUNT(*) FROM silver.pessoa_origem o LEFT JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id WHERE p.pessoa_origem_id IS NULL;").Replace(' ', '')
    if ($missing -ne '0') { throw "Backfill progressivo incompleto antes do cutover: faltantes=$missing." }
}

Invoke-Compose @('up', '-d', 'sqlserver')
Wait-SqlHealthy
Invoke-Sql @('-Q', "IF DB_ID(N'$Db') IS NOT NULL BEGIN ALTER DATABASE [$Db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Db]; END; CREATE DATABASE [$Db];") | Out-Null

# Origem histórica real do teste de upgrade.
Invoke-Sql @('-d', $Db, '-i', $BaselineRel) | Out-Null
Invoke-Sql @('-d', $Db, '-Q', "INSERT ref.gestor(codigo,nome,ativo) VALUES('ZZ_UPGRADE_SENTINEL','Sentinela DDL Upgrade',1);") | Out-Null
Invoke-Sql @('-d', $Db, '-i', $BaselineSeedRel) | Out-Null
Invoke-Sql @('-d', $Db, '-Q', "DECLARE @id BIGINT=(SELECT pessoa_atributo_observacao_id FROM silver.pessoa_atributo_observacao WHERE source_record_id='SEH001-TEL-1'); UPDATE silver.pessoa_atributo_observacao SET valor=N'00 55 11 99999-0001',atributo_instancia_chave='005511999990001' WHERE pessoa_atributo_observacao_id=@id; UPDATE gold.pessoa_atributo SET valor=N'00 55 11 99999-0001',atributo_instancia_chave='005511999990001' WHERE pessoa_atributo_observacao_id=@id AND vigencia_fim IS NULL;") | Out-Null
Invoke-Sql @('-d', $Db, '-Q', "DECLARE @id BIGINT=(SELECT pessoa_atributo_observacao_id FROM silver.pessoa_atributo_observacao WHERE source_record_id='SEH002-EMAIL-1'); UPDATE silver.pessoa_atributo_observacao SET valor=N'JOSÉ@EXAMPLE.ORG',atributo_instancia_chave=N'josé@example.org' WHERE pessoa_atributo_observacao_id=@id; UPDATE gold.pessoa_atributo SET valor=N'JOSÉ@EXAMPLE.ORG',atributo_instancia_chave=N'josé@example.org' WHERE pessoa_atributo_observacao_id=@id AND vigencia_fim IS NULL;") | Out-Null

$baselineHash = Get-Fingerprint 'baseline'
$beforeInvariant = Join-Path $OutDir 'invariants-before.json'
$afterInvariant = Join-Path $OutDir 'invariants-after.json'
Write-InvariantSnapshot $beforeInvariant

Invoke-ProgressiveIdentityBackfill
Invoke-Sql @('-d', $Db, '-i', $CurrentRel) | Out-Null
Assert-Sentinel
Assert-PhoneV2
Assert-EmailV2
Assert-SchemaMarker
Invoke-Sql @('-d', $Db, '-i', 'database/Jornada_Runtime_Smoke.sql') | Out-Null
$firstHash = Get-Fingerprint 'current-first'

Invoke-Sql @('-d', $Db, '-i', $CurrentRel) | Out-Null
Assert-Sentinel
$secondHash = Get-Fingerprint 'current-second'
Write-InvariantSnapshot $afterInvariant

Push-Location $Root
try {
    & $Python.Source scripts/upgrade-invariant-gate.py $beforeInvariant $afterInvariant --summary (Join-Path $OutDir 'invariant-summary.json')
    if ($LASTEXITCODE -ne 0) { throw 'Upgrade invariant gate falhou.' }
}
finally { Pop-Location }

if ($firstHash -ne $secondHash) { throw 'Fingerprint do DDL mudou na segunda aplicação; idempotência violada.' }

@(
    "baseline=$BaselineRel",
    "baseline_seed=$BaselineSeedRel",
    "current=$CurrentRel",
    "baseline_fingerprint=$baselineHash",
    "current_first_fingerprint=$firstHash",
    "current_second_fingerprint=$secondHash",
    'progressive_identity_backfill=ProgressiveIdentityOriginStore.BackfillPageAsync',
    'progressive_identity_page_size=1000',
    "sql_host_port=$port",
    'sentinel_preserved=true',
    'phone_v2_legacy_00_migrated=true',
    'email_v2_legacy_migrated=true',
    'schema_marker_exact=true',
    'runtime_smoke=true',
    'upgrade_invariants=true',
    'idempotent=true'
) | Set-Content -Encoding UTF8 (Join-Path $OutDir 'result.txt')

Get-Content (Join-Path $OutDir 'result.txt')
Write-Host 'DDL UPGRADE GATE: OK'
