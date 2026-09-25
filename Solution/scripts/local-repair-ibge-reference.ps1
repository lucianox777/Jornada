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
if ($expectedCode -notmatch '^[A-Za-z0-9_-]+$') { throw "referenceCode invalido: $expectedCode" }
if ($expectedCode -ne [string]$projection.referenceCode) { throw 'Manifestos IBGE divergem no referenceCode.' }

$expectedRows = [long](($projection.files | Measure-Object -Property rowCount -Sum).Sum)
if ($expectedRows -le 0) { throw 'Projection manifest IBGE nao possui rowCount total valido.' }

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
    $previousErrorActionPreference = $ErrorActionPreference
    $previousSqlcmdPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
    Push-Location $Root
    try {
        $env:SQLCMDPASSWORD = $password
        $ErrorActionPreference = 'Continue'
        & docker compose --env-file $EnvFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d master -Q 'SET NOCOUNT ON; SELECT 1;' *> $null
        return $LASTEXITCODE -eq 0
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        if ($null -eq $previousSqlcmdPassword) {
            Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
        } else {
            $env:SQLCMDPASSWORD = $previousSqlcmdPassword
        }
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
        [Parameter(Mandatory = $true)][string]$Query
    )
    $displayQuery = ($Query -replace '\s+', ' ').Trim()
    Write-Host "# sqlcmd -d $Database -Q `"$displayQuery`""
    $previousSqlcmdPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
    Push-Location $Root
    try {
        $env:SQLCMDPASSWORD = $password
        $raw = @(& docker compose --env-file $EnvFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $Database -W -h -1 -Q "SET NOCOUNT ON; $Query")
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd falhou ($LASTEXITCODE)." }
        $value = @($raw | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ } | Select-Object -Last 1)
        if ($value.Count -eq 0) { throw 'Consulta SQL nao retornou valor.' }
        return [string]$value[0]
    }
    finally {
        if ($null -eq $previousSqlcmdPassword) {
            Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
        } else {
            $env:SQLCMDPASSWORD = $previousSqlcmdPassword
        }
        Pop-Location
    }
}

function Invoke-SqlNonQuery {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Query
    )
    Write-Host "# sqlcmd -d $Database -Q <reativacao transacional da referencia IBGE>"
    $previousSqlcmdPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
    Push-Location $Root
    try {
        $env:SQLCMDPASSWORD = $password
        & docker compose --env-file $EnvFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $Database -Q $Query
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd falhou ($LASTEXITCODE)." }
    }
    finally {
        if ($null -eq $previousSqlcmdPassword) {
            Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
        } else {
            $env:SQLCMDPASSWORD = $previousSqlcmdPassword
        }
        Pop-Location
    }
}

if (-not $NoStart) {
    Invoke-Compose -ComposeArgs @('up', '-d', 'sqlserver')
}

Wait-SqlReady

$dbExists = Invoke-SqlScalar -Database 'master' -Query "SELECT CASE WHEN DB_ID(N'$db') IS NULL THEN 0 ELSE 1 END;"
if ($dbExists -ne '1') { throw "Banco $db nao existe." }

$schemaReady = Invoke-SqlScalar -Database $db -Query "SELECT CASE WHEN OBJECT_ID(N'ref.frequencia_nome',N'U') IS NOT NULL AND OBJECT_ID(N'ref.frequencia_nome_versao',N'U') IS NOT NULL THEN 1 ELSE 0 END;"
if ($schemaReady -ne '1') { throw 'Tabelas de referencia IBGE ausentes.' }

$activeCount = [int](Invoke-SqlScalar -Database $db -Query "SELECT COUNT(*) FROM ref.frequencia_nome_versao WHERE status=N'ATIVA';")
if ($activeCount -eq 1) {
    $activeCode = Invoke-SqlScalar -Database $db -Query "SELECT TOP(1) codigo FROM ref.frequencia_nome_versao WHERE status=N'ATIVA';"
    if ($activeCode -eq $expectedCode) {
        Write-Host ''
        Write-Host "IBGE REFERENCE REPAIR: nada a fazer; $expectedCode ja esta ATIVA." -ForegroundColor Green
        exit 0
    }
    throw "Existe outra referencia ATIVA ($activeCode). A troca de referencia deve ser explicita."
}
if ($activeCount -ne 0) { throw "Estado invalido: referencias ATIVAS=$activeCount." }

$canonicalCount = [int](Invoke-SqlScalar -Database $db -Query "SELECT COUNT(*) FROM ref.frequencia_nome_versao WHERE codigo=N'$expectedCode';")
if ($canonicalCount -ne 1) {
    Write-Host ''
    Write-Warning "Versao canonica $expectedCode encontrada=$canonicalCount. Executando diagnostico read-only antes de abortar o reparo."
    Write-Host '# .\scripts\local-diagnose-ibge-reference.ps1 -NoStart'
    & (Join-Path $PSScriptRoot 'local-diagnose-ibge-reference.ps1') -NoStart
    throw "Nao e seguro criar/adotar automaticamente uma versao canonica ausente. Use o diagnostico acima para decidir entre migracao de referencia legada e carga canonica."
}

$status = Invoke-SqlScalar -Database $db -Query "SELECT status FROM ref.frequencia_nome_versao WHERE codigo=N'$expectedCode';"
if ($status -notin @('OBSOLETA','VALIDADA')) {
    throw "Referencia canonica nao pode ser reativada deste estado: status=$status."
}

$hash = Invoke-SqlScalar -Database $db -Query "SELECT ISNULL(CONVERT(VARCHAR(64),conteudo_sha256,2),N'NULL') FROM ref.frequencia_nome_versao WHERE codigo=N'$expectedCode';"
if ($hash -notmatch '^[0-9A-Fa-f]{64}$') { throw 'Referencia canonica nao possui conteudo_sha256 valido.' }

$rows = [long](Invoke-SqlScalar -Database $db -Query "SELECT COUNT_BIG(*) FROM ref.frequencia_nome f JOIN ref.frequencia_nome_versao v ON v.frequencia_nome_versao_id=f.frequencia_nome_versao_id WHERE v.codigo=N'$expectedCode';")
if ($rows -ne $expectedRows) {
    throw "Referencia canonica nao sera reativada: linhas no banco=$rows; esperado pelo projection-manifest=$expectedRows."
}

$nameRows = [long](Invoke-SqlScalar -Database $db -Query "SELECT COUNT_BIG(*) FROM ref.frequencia_nome f JOIN ref.frequencia_nome_versao v ON v.frequencia_nome_versao_id=f.frequencia_nome_versao_id WHERE v.codigo=N'$expectedCode' AND f.tipo=N'NOME';")
$surnameRows = [long](Invoke-SqlScalar -Database $db -Query "SELECT COUNT_BIG(*) FROM ref.frequencia_nome f JOIN ref.frequencia_nome_versao v ON v.frequencia_nome_versao_id=f.frequencia_nome_versao_id WHERE v.codigo=N'$expectedCode' AND f.tipo=N'SOBRENOME';")
if ($nameRows -le 0 -or $surnameRows -le 0) {
    throw "Referencia canonica incompleta: NOME=$nameRows; SOBRENOME=$surnameRows."
}

$immutableTriggers = [int](Invoke-SqlScalar -Database $db -Query "SELECT COUNT(*) FROM sys.triggers WHERE is_disabled=0 AND name IN(N'tr_frequencia_nome_bloqueia_versao_publicada',N'tr_frequencia_nome_versao_metadado_immutavel');")
if ($immutableTriggers -ne 2) { throw "Protecao de imutabilidade incompleta: triggers=$immutableTriggers/2." }

$reactivateSql = @"
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;

IF EXISTS(SELECT 1 FROM ref.frequencia_nome_versao WITH(UPDLOCK,HOLDLOCK) WHERE status=N'ATIVA')
    THROW 51660,'Ja existe referencia ATIVA; reativacao abortada.',1;

UPDATE ref.frequencia_nome_versao
SET status=N'ATIVA',
    ativado_em=SYSDATETIMEOFFSET()
WHERE codigo=N'$expectedCode'
  AND status IN(N'OBSOLETA',N'VALIDADA')
  AND conteudo_sha256 IS NOT NULL;

IF @@ROWCOUNT<>1
    THROW 51661,'Referencia canonica nao foi reativada.',1;

COMMIT TRANSACTION;
"@

Invoke-SqlNonQuery -Database $db -Query $reactivateSql

Write-Host ''
Write-Host "Referencia $expectedCode reativada sem recarga de dados." -ForegroundColor Green
Write-Host "  linhas preservadas: $rows"
Write-Host "  sha256 preservado: $hash"
Write-Host '  dados alterados: somente status/ativado_em em ref.frequencia_nome_versao'
Write-Host ''
Write-Host '# .\scripts\local-check-ibge-reference.ps1 -NoStart'
& (Join-Path $PSScriptRoot 'local-check-ibge-reference.ps1') -NoStart
