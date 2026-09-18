param(
    [switch]$NoStart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'

if (-not (Test-Path -LiteralPath $EnvFile -PathType Leaf)) {
    throw '.env local nao encontrado.'
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

if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD nao definido.' }
if ($db -notmatch '^[A-Za-z0-9_]+$') { throw 'JORNADA_SQL_DATABASE invalido.' }

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

function Invoke-Sql {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Query
    )
    Write-Host "# sqlcmd -d $Database -Q <diagnostico read-only>"
    Push-Location $Root
    try {
        & docker compose --env-file $EnvFile exec -T -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $Database -W -s '|' -Q "SET NOCOUNT ON; $Query"
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd falhou ($LASTEXITCODE)." }
    }
    finally { Pop-Location }
}

if (-not $NoStart) { Invoke-Compose -ComposeArgs @('up', '-d', 'sqlserver') }

Write-Host ''
Write-Host '=== IBGE REFERENCE DIAGNOSTIC ==='
Write-Host ''

Invoke-Sql -Database 'master' -Query "SELECT DB_NAME(database_id) AS database_name,state_desc FROM sys.databases WHERE name=N'$db';"

Invoke-Sql -Database $db -Query "SELECT CASE WHEN OBJECT_ID(N'ref.frequencia_nome_versao',N'U') IS NULL THEN 0 ELSE 1 END AS versao_table, CASE WHEN OBJECT_ID(N'ref.frequencia_nome',N'U') IS NULL THEN 0 ELSE 1 END AS frequencia_table, CASE WHEN OBJECT_ID(N'ref.frequencia_nome_cobertura',N'U') IS NULL THEN 0 ELSE 1 END AS cobertura_table;"

$versionQuery = @'
IF OBJECT_ID(N''ref.frequencia_nome_versao'',N''U'') IS NOT NULL
BEGIN
    SELECT
        v.frequencia_nome_versao_id AS versao_id,
        v.codigo,
        v.status,
        CONVERT(VARCHAR(64),v.conteudo_sha256,2) AS sha256,
        CONVERT(VARCHAR(33),v.criado_em,126) AS criado_em,
        CONVERT(VARCHAR(33),v.ativado_em,126) AS ativado_em,
        COUNT_BIG(f.frequencia_nome_id) AS linhas,
        SUM(CASE WHEN f.tipo=N''NOME'' THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END) AS linhas_nome,
        SUM(CASE WHEN f.tipo=N''SOBRENOME'' THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END) AS linhas_sobrenome
    FROM ref.frequencia_nome_versao v
    LEFT JOIN ref.frequencia_nome f ON f.frequencia_nome_versao_id=v.frequencia_nome_versao_id
    GROUP BY v.frequencia_nome_versao_id,v.codigo,v.status,v.conteudo_sha256,v.criado_em,v.ativado_em
    ORDER BY v.frequencia_nome_versao_id;
END
'@
Invoke-Sql -Database $db -Query $versionQuery

$totalsQuery = @'
IF OBJECT_ID(N''ref.frequencia_nome'',N''U'') IS NOT NULL
BEGIN
    SELECT
        COUNT_BIG(*) AS total_linhas,
        SUM(CASE WHEN tipo=N''NOME'' THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END) AS total_nome,
        SUM(CASE WHEN tipo=N''SOBRENOME'' THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END) AS total_sobrenome,
        SUM(CASE WHEN escopo_geografico=N''BRASIL'' THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END) AS total_brasil,
        SUM(CASE WHEN escopo_geografico=N''UF'' THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END) AS total_uf,
        SUM(CASE WHEN escopo_geografico=N''MUNICIPIO'' THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END) AS total_municipio
    FROM ref.frequencia_nome;
END
'@
Invoke-Sql -Database $db -Query $totalsQuery

$orphanQuery = @'
IF OBJECT_ID(N''ref.frequencia_nome'',N''U'') IS NOT NULL AND OBJECT_ID(N''ref.frequencia_nome_versao'',N''U'') IS NOT NULL
BEGIN
    SELECT COUNT_BIG(*) AS linhas_sem_versao
    FROM ref.frequencia_nome f
    LEFT JOIN ref.frequencia_nome_versao v ON v.frequencia_nome_versao_id=f.frequencia_nome_versao_id
    WHERE v.frequencia_nome_versao_id IS NULL;
END
'@
Invoke-Sql -Database $db -Query $orphanQuery

Write-Host ''
Write-Host 'DIAGNOSTICO CONCLUIDO: nenhuma alteracao foi feita.' -ForegroundColor Green
Write-Host 'Cole a tabela de versoes acima se precisar de analise.'
