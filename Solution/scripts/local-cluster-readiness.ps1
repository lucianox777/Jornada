param(
    [string]$DatabaseName = 'JornadaLocal',
    [string]$EnvFile = ''
)
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($EnvFile)) {
    # Não herda JORNADA_LOCAL_ENV_FILE: o ensaio isolado pode tê-lo deixado ativo.
    $EnvFile = Join-Path $Root '.env'
}
$EnvFile = [IO.Path]::GetFullPath($EnvFile)
if (-not (Test-Path -LiteralPath $EnvFile -PathType Leaf)) { throw "Configuração não encontrada: $EnvFile" }
if ($DatabaseName -notmatch '^[A-Za-z][A-Za-z0-9_]{0,100}$') { throw 'Nome de banco inválido.' }
$values = @{}
Get-Content -LiteralPath $EnvFile | ForEach-Object {
    if ($_ -match '^\s*([^#=\s]+)\s*=(.*)$') { $values[$matches[1]] = $matches[2].Trim().Trim('"') }
}
if ($values['JORNADA_SQL_DATABASE'] -ne $DatabaseName) {
    throw "O .env não aponta para $DatabaseName. Nenhuma alteração foi feita."
}
$password = $values['JORNADA_SQL_SA_PASSWORD']
if ([string]::IsNullOrWhiteSpace($password)) { throw 'Senha SQL não configurada.' }
$config = Get-Content -LiteralPath (Join-Path $Root 'install/windows-production/Jornada.Cluster.Test.json') -Raw | ConvertFrom-Json
$bundle = [string]$config.configurationBundleVersion
$schema = [string]$config.solutionSchema
if ($bundle -notmatch '^[A-Za-z0-9._-]+$' -or $schema -notmatch '^[A-Za-z0-9._-]+$') {
    throw 'Bundle/schema inválidos no contrato de cluster.'
}
function Invoke-Compose([string[]]$Arguments) {
    & docker compose --env-file $EnvFile @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Verificação Docker Compose falhou ($LASTEXITCODE)." }
}
Push-Location $Root
$marker = '.jornada-readiness-' + [Guid]::NewGuid().ToString('N')
$nonce = [Guid]::NewGuid().ToString('N')
try {
    Write-Host "Cluster original: $DatabaseName; preflight sem alterações SQL."
    foreach ($node in @('jornada-node1','jornada-node2')) {
        Invoke-Compose @('exec','-T',$node,'sh','-c',
            'case "$ConnectionStrings__Jornada" in *"Database=$1;"*) exit 0;; *) exit 12;; esac',
            'sh',$DatabaseName)
    }
    $sql = @"
SET NOCOUNT ON;
IF DB_NAME()<>N'$DatabaseName' THROW 51860,'Banco divergente.',1;
IF ISNULL(CONVERT(NVARCHAR(32),(
  SELECT value FROM sys.extended_properties
  WHERE class=0 AND name=N'Jornada.EnvironmentProfile')),N'')<>N'Development'
  THROW 51861,'Apenas banco Development autorizado.',1;
IF ISNULL(CONVERT(NVARCHAR(32),(
  SELECT value FROM sys.extended_properties
  WHERE class=0 AND name=N'Jornada.SolutionSchema')),N'')<>N'$schema'
  THROW 51862,'Schema divergente.',1;
IF (SELECT COUNT(*) FROM controle.runtime_componente
    WHERE node_id IN(N'NODE1',N'NODE2')
      AND componente IN(N'Api',N'Processor')
      AND status=N'RUNNING'
      AND heartbeat_em>=DATEADD(SECOND,-35,SYSUTCDATETIME())
      AND configuration_bundle_version=N'$bundle'
      AND solution_schema_expected=N'$schema')<>4
  THROW 51863,'NODE1/NODE2: API/Processor offline, bundle/schema divergente ou heartbeat vencido.',1;
SELECT node_id,componente,configuration_bundle_version,heartbeat_em
FROM controle.runtime_componente
WHERE node_id IN(N'NODE1',N'NODE2') AND componente IN(N'Api',N'Processor')
ORDER BY node_id,componente;
SELECT status,COUNT_BIG(*) quantidade FROM ingestao.lote GROUP BY status ORDER BY status;
SELECT COUNT_BIG(*) objetos_bronze_disponiveis FROM bronze.entrega_arquivo
WHERE estado_armazenamento=N'DISPONIVEL';
"@
    & docker compose --env-file $EnvFile exec -T -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $DatabaseName -w 220 -Q $sql
    if ($LASTEXITCODE -ne 0) { throw 'Preflight SQL do cluster reprovado.' }
    foreach ($url in @('http://127.0.0.1:5080/health/ready','http://127.0.0.1:5180/health/ready')) {
        $r = Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 8
        if ([int]$r.StatusCode -ne 200) { throw "Health/ready falhou: $url" }
    }
    # Somente um marcador efêmero FORA de sha256/, não um objeto Bronze real.
    Invoke-Compose @('exec','-T','jornada-node1','sh','-c',
        'printf "%s" "$2" > "/data/bronze/$1"','sh',$marker,$nonce)
    Invoke-Compose @('exec','-T','jornada-node2','sh','-c',
        'test "$(cat "/data/bronze/$1")" = "$2" && printf ":node2" >> "/data/bronze/$1"',
        'sh',$marker,$nonce)
    Invoke-Compose @('exec','-T','jornada-node1','sh','-c',
        'test "$(cat "/data/bronze/$1")" = "$2:node2"','sh',$marker,$nonce)
    Write-Host 'PASS: NODE1 e NODE2 compartilham banco original e volume Bronze NAS. Nenhum dado SQL foi modificado.' -ForegroundColor Green
}
finally {
    foreach ($node in @('jornada-node1','jornada-node2')) {
        & docker compose --env-file $EnvFile exec -T $node sh -c 'rm -f "/data/bronze/$1"' 'sh' $marker *> $null
    }
    Pop-Location
}
