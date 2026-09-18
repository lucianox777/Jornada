param(
    [switch]$NoStart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
$ManifestPath = Join-Path $Root 'data\reference\ibge-nomes-2022\manifest.json'
$ProjectionManifestPath = Join-Path $Root 'data\reference\ibge-nomes-2022\projection-manifest.json'

if (-not (Test-Path -LiteralPath $EnvFile -PathType Leaf)) {
    throw '.env local não encontrado. Este check reutiliza o banco existente e não cria configuração.'
}
if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Manifesto IBGE não encontrado: $ManifestPath"
}
if (-not (Test-Path -LiteralPath $ProjectionManifestPath -PathType Leaf)) {
    throw "Projection manifest IBGE não encontrado: $ProjectionManifestPath"
}

$vars = @{}
Get-Content -LiteralPath $EnvFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
        $parts = $line.Split('=', 2)
        $vars[$parts[0].Trim()] = $parts[1]
    }
}

$password = [string]$vars['JORNADA_SQL_SA_PASSWORD']
$db = if ($vars['JORNADA_SQL_DATABASE']) { [string]$vars['JORNADA_SQL_DATABASE'] } else { 'JornadaLocal' }
if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD não definido.' }
if ($db -notmatch '^[A-Za-z0-9_]+$') { throw 'JORNADA_SQL_DATABASE inválido.' }

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$projection = Get-Content -LiteralPath $ProjectionManifestPath -Raw | ConvertFrom-Json
$expectedCode = [string]$manifest.referenceCode
$projectionCode = [string]$projection.referenceCode
if ([string]::IsNullOrWhiteSpace($expectedCode) -or $expectedCode -ne $projectionCode) {
    throw "Manifestos IBGE divergentes: manifest=$expectedCode projection=$projectionCode."
}

$expectedRows = [long](($projection.files | Measure-Object -Property rowCount -Sum).Sum)
if ($expectedRows -le 0) { throw 'Projection manifest IBGE não contém rowCount total válido.' }

function Invoke-Compose {
    param([Parameter(Mandatory = $true)][string[]]$ComposeArgs)

    Write-Host ("# docker compose --env-file .env " + ($ComposeArgs -join ' '))
    Push-Location $Root
    try {
        & docker compose --env-file $EnvFile @ComposeArgs
        if ($LASTEXITCODE -ne 0) { throw "docker compose falhou ($LASTEXITCODE)." }
    }
    finally {
        Pop-Location
    }
}

function Test-SqlReady {
    $previousErrorActionPreference = $ErrorActionPreference
    Push-Location $Root
    try {
        $ErrorActionPreference = 'Continue'
        & docker compose --env-file $EnvFile exec -T -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d master -Q 'SET NOCOUNT ON; SELECT 1;' *> $null
        return $LASTEXITCODE -eq 0
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        Pop-Location
    }
}

function Wait-SqlReady {
    for ($attempt = 1; $attempt -le 45; $attempt++) {
        if (Test-SqlReady) { return }
        Start-Sleep -Seconds 2
    }
    throw 'SQL Server local não ficou pronto. O check não recria banco nem carrega a referência IBGE.'
}

function Invoke-SqlScalar {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Query
    )

    $displayQuery = ($Query -replace '\s+', ' ').Trim()
    Write-Host "# sqlcmd -d $Database -Q `"$displayQuery`""
    Push-Location $Root
    try {
        $raw = @(& docker compose --env-file $EnvFile exec -T -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $Database -W -h -1 -Q "SET NOCOUNT ON; $Query")
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd falhou ($LASTEXITCODE)." }
        $value = @($raw | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ } | Select-Object -Last 1)
        if ($value.Count -eq 0) { throw 'Consulta SQL não retornou valor.' }
        return [string]$value[0]
    }
    finally {
        Pop-Location
    }
}

if (-not $NoStart) {
    # Apenas sobe o container/volume existente. Não chama local-db.ps1 up/reset,
    # não reaplica seed e não executa o loader de milhões de linhas do IBGE.
    Invoke-Compose -ComposeArgs @('up', '-d', 'sqlserver')
}

Wait-SqlReady

$dbExists = Invoke-SqlScalar -Database 'master' -Query "SELECT CASE WHEN DB_ID(N'$db') IS NULL THEN 0 ELSE 1 END;"
if ($dbExists -ne '1') {
    throw "Banco $db não existe. Este check é deliberadamente read-only e não materializa a referência IBGE."
}

$schemaReady = Invoke-SqlScalar -Database $db -Query "SELECT CASE WHEN OBJECT_ID(N'ref.frequencia_nome',N'U') IS NOT NULL AND OBJECT_ID(N'ref.frequencia_nome_versao',N'U') IS NOT NULL THEN 1 ELSE 0 END;"
if ($schemaReady -ne '1') {
    throw 'Tabelas ref.frequencia_nome/ref.frequencia_nome_versao ausentes. O check não executa migração nem carga.'
}

$activeCount = [int](Invoke-SqlScalar -Database $db -Query "SELECT COUNT(*) FROM ref.frequencia_nome_versao WHERE status=N'ATIVA';")
if ($activeCount -ne 1) {
    throw "Referência IBGE inválida: esperado exatamente 1 registro ATIVO; encontrado=$activeCount."
}

$actualCode = Invoke-SqlScalar -Database $db -Query "SELECT TOP(1) codigo FROM ref.frequencia_nome_versao WHERE status=N'ATIVA';"
if ($actualCode -ne $expectedCode) {
    throw "Referência IBGE mudou em relação ao repositório: banco=$actualCode; esperado=$expectedCode. Execute a carga/upgrade explicitamente antes de testar."
}

$actualHash = Invoke-SqlScalar -Database $db -Query "SELECT TOP(1) CONVERT(VARCHAR(64),conteudo_sha256,2) FROM ref.frequencia_nome_versao WHERE status=N'ATIVA';"
if ($actualHash -notmatch '^[0-9A-Fa-f]{64}$') {
    throw 'Referência IBGE ATIVA não possui conteudo_sha256 válido.'
}

$actualRows = [long](Invoke-SqlScalar -Database $db -Query "SELECT COUNT_BIG(*) FROM ref.frequencia_nome f JOIN ref.frequencia_nome_versao v ON v.frequencia_nome_versao_id=f.frequencia_nome_versao_id WHERE v.status=N'ATIVA';")
if ($actualRows -ne $expectedRows) {
    throw "Quantidade de linhas da referência IBGE mudou: banco=$actualRows; projection-manifest=$expectedRows."
}

$immutableTriggers = [int](Invoke-SqlScalar -Database $db -Query "SELECT COUNT(*) FROM sys.triggers WHERE is_disabled=0 AND name IN(N'tr_frequencia_nome_bloqueia_versao_publicada',N'tr_frequencia_nome_versao_metadado_immutavel');")
if ($immutableTriggers -ne 2) {
    throw "Proteção de imutabilidade da referência IBGE não está íntegra: triggers habilitados=$immutableTriggers/2."
}

Write-Host ''
Write-Host 'IBGE REFERENCE QUICK CHECK: OK' -ForegroundColor Green
Write-Host "  versao: $actualCode"
Write-Host "  linhas: $actualRows"
Write-Host "  sha256 publicado: $actualHash"
Write-Host '  imutabilidade: OK (2/2 triggers habilitados)'
Write-Host '  carga executada: NAO'
