param(
    [switch]$NoStart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
$ManifestPath = Join-Path $Root 'data\reference\ibge-nomes-2022\manifest.json'
$ProjectionManifestPath = Join-Path $Root 'data\reference\ibge-nomes-2022\projection-manifest.json'

if (-not (Test-Path -LiteralPath $EnvFile -PathType Leaf)) { throw '.env local nao encontrado.' }
if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) { throw "Manifesto IBGE nao encontrado: $ManifestPath" }
if (-not (Test-Path -LiteralPath $ProjectionManifestPath -PathType Leaf)) { throw "Projection manifest IBGE nao encontrado: $ProjectionManifestPath" }

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
if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD nao definido.' }
if ($db -notmatch '^[A-Za-z0-9_]+$') { throw 'JORNADA_SQL_DATABASE invalido.' }

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$projection = Get-Content -LiteralPath $ProjectionManifestPath -Raw | ConvertFrom-Json
$expectedCode = [string]$manifest.referenceCode
if ($expectedCode -ne [string]$projection.referenceCode) { throw 'Manifestos IBGE divergem no referenceCode.' }
$expectedRows = [long](($projection.files | Measure-Object -Property rowCount -Sum).Sum)
if ($expectedRows -le 0) { throw 'Projection manifest IBGE nao contem rowCount total valido.' }

function Invoke-Compose {
    param([Parameter(Mandatory = $true)][string[]]$ComposeArgs)
    Write-Host ("# docker compose --env-file .env " + ($ComposeArgs -join ' '))
    Push-Location $Root
    try {
        & docker compose --env-file $EnvFile @ComposeArgs
        if ($LASTEXITCODE -ne 0) { throw "docker compose falhou ($LASTEXITCODE)." }
    }
    finally { Pop-Location }
}

function Test-SqlReady {
    $previous = $ErrorActionPreference
    Push-Location $Root
    try {
        $ErrorActionPreference = 'Continue'
        & docker compose --env-file $EnvFile exec -T -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d master -Q 'SET NOCOUNT ON; SELECT 1;' *> $null
        return $LASTEXITCODE -eq 0
    }
    finally {
        $ErrorActionPreference = $previous
        Pop-Location
    }
}

function Wait-SqlReady {
    for ($attempt = 1; $attempt -le 45; $attempt++) {
        if (Test-SqlReady) { return }
        Start-Sleep -Seconds 2
    }
    throw 'SQL Server local nao ficou pronto.'
}

function Invoke-SqlScalar {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Query,
        [switch]$Quiet
    )
    if (-not $Quiet) { Write-Host "# sqlcmd -d $Database -Q <quick-check read-only>" }
    Push-Location $Root
    try {
        $raw = @(& docker compose --env-file $EnvFile exec -T -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $Database -W -h -1 -Q "SET NOCOUNT ON; $Query")
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd falhou ($LASTEXITCODE)." }
        $value = @($raw | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ } | Select-Object -Last 1)
        if ($value.Count -eq 0) { return '' }
        return [string]$value[0]
    }
    finally { Pop-Location }
}

function Wait-DatabaseOnline {
    $lastState = ''
    for ($attempt = 1; $attempt -le 120; $attempt++) {
        $state = Invoke-SqlScalar -Database 'master' -Quiet -Query "SELECT COALESCE((SELECT state_desc FROM sys.databases WHERE name=N'$db'),N'AUSENTE');"
        $lastState = $state
        if ($state -eq 'ONLINE' -or $state -eq 'AUSENTE') { return }
        if ($attempt -eq 1 -or ($attempt % 10) -eq 0) {
            Write-Host "Aguardando $db ficar ONLINE para quick check IBGE (estado=$state; tentativa=$attempt/120)..."
        }
        Start-Sleep -Seconds 2
    }
    throw "Banco $db nao ficou ONLINE. Ultimo estado=$lastState."
}

if (-not $NoStart) { Invoke-Compose -ComposeArgs @('up','-d','sqlserver') }
Wait-SqlReady
Wait-DatabaseOnline

$dbExists = Invoke-SqlScalar -Database 'master' -Query "SELECT CASE WHEN DB_ID(N'$db') IS NULL THEN 0 ELSE 1 END;"
if ($dbExists -ne '1') { throw "Banco $db nao existe. O check nao materializa a referencia IBGE." }

$schemaReady = Invoke-SqlScalar -Database $db -Query "SELECT CASE WHEN OBJECT_ID(N'ref.frequencia_nome',N'U') IS NOT NULL AND OBJECT_ID(N'ref.frequencia_nome_versao',N'U') IS NOT NULL THEN 1 ELSE 0 END;"
if ($schemaReady -ne '1') { throw 'Tabelas de referencia IBGE ausentes.' }

$activeCount = [int](Invoke-SqlScalar -Database $db -Query "SELECT COUNT(*) FROM ref.frequencia_nome_versao WHERE status=N'ATIVA';")
if ($activeCount -ne 1) {
    $canonicalExists = [int](Invoke-SqlScalar -Database $db -Query "SELECT COUNT(*) FROM ref.frequencia_nome_versao WHERE codigo=N'$expectedCode';")
    if ($canonicalExists -eq 1) {
        $canonicalStatus = Invoke-SqlScalar -Database $db -Query "SELECT status FROM ref.frequencia_nome_versao WHERE codigo=N'$expectedCode';"
        $canonicalHash = Invoke-SqlScalar -Database $db -Query "SELECT ISNULL(CONVERT(VARCHAR(64),conteudo_sha256,2),N'NULL') FROM ref.frequencia_nome_versao WHERE codigo=N'$expectedCode';"
        $canonicalRows = [long](Invoke-SqlScalar -Database $db -Query "SELECT COUNT_BIG(*) FROM ref.frequencia_nome f JOIN ref.frequencia_nome_versao v ON v.frequencia_nome_versao_id=f.frequencia_nome_versao_id WHERE v.codigo=N'$expectedCode';")
        Write-Warning "Referencia canonica nao ATIVA: status=$canonicalStatus; linhas=$canonicalRows; esperado=$expectedRows; sha256=$canonicalHash."

        if ($canonicalStatus -in @('OBSOLETA','VALIDADA') -and $canonicalRows -eq $expectedRows -and $canonicalHash -match '^[0-9A-Fa-f]{64}$') {
            throw "Referencia integra, mas inativa. Execute .\scripts\local-repair-ibge-reference.ps1."
        }
        if ($canonicalStatus -eq 'CARREGANDO') {
            throw "Referencia em CARREGANDO. Execute .\scripts\local-load-ibge-reference.ps1 -AllowLoad."
        }
    }
    throw "Referencia IBGE invalida: esperado exatamente 1 registro ATIVO; encontrado=$activeCount."
}

$actualCode = Invoke-SqlScalar -Database $db -Query "SELECT TOP(1) codigo FROM ref.frequencia_nome_versao WHERE status=N'ATIVA';"
if ($actualCode -ne $expectedCode) { throw "Referencia IBGE mudou: banco=$actualCode; esperado=$expectedCode." }

$actualHash = Invoke-SqlScalar -Database $db -Query "SELECT TOP(1) CONVERT(VARCHAR(64),conteudo_sha256,2) FROM ref.frequencia_nome_versao WHERE status=N'ATIVA';"
if ($actualHash -notmatch '^[0-9A-Fa-f]{64}$') { throw 'Referencia IBGE ATIVA sem SHA-256 valido.' }

$actualRows = [long](Invoke-SqlScalar -Database $db -Query "SELECT COUNT_BIG(*) FROM ref.frequencia_nome f JOIN ref.frequencia_nome_versao v ON v.frequencia_nome_versao_id=f.frequencia_nome_versao_id WHERE v.status=N'ATIVA';")
if ($actualRows -ne $expectedRows) { throw "Quantidade de linhas mudou: banco=$actualRows; esperado=$expectedRows." }

$immutableTriggers = [int](Invoke-SqlScalar -Database $db -Query "SELECT COUNT(*) FROM sys.triggers WHERE is_disabled=0 AND name IN(N'tr_frequencia_nome_bloqueia_versao_publicada',N'tr_frequencia_nome_versao_metadado_immutavel');")
if ($immutableTriggers -ne 2) { throw "Protecao de imutabilidade incompleta: $immutableTriggers/2 triggers." }

Write-Host ''
Write-Host 'IBGE REFERENCE QUICK CHECK: OK' -ForegroundColor Green
Write-Host "  versao: $actualCode"
Write-Host "  linhas: $actualRows"
Write-Host "  sha256 publicado: $actualHash"
Write-Host '  imutabilidade: OK (2/2 triggers habilitados)'
Write-Host '  carga executada: NAO'
