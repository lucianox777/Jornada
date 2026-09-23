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
# Em Windows PowerShell 5.1, argumentos entre aspas dentro de sh -c podem
# perder aspas ao atravessar a CLI nativa. Use docker inspect/cp/exec diretamente:
# nenhum shell remoto interpreta ;, $, redirecionamento nem caminho de volume.
Push-Location $Root
$marker = '.jornada-readiness-' + [Guid]::NewGuid().ToString('N')
$nonce = [Guid]::NewGuid().ToString('N')
$containers = @{}
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('jornada-cluster-readiness-' + $nonce)
try {
    Write-Host "Cluster original: $DatabaseName; preflight sem alterações SQL."
    foreach ($node in @('jornada-node1','jornada-node2')) {
        $containerId = (& docker compose --env-file $EnvFile ps -q $node | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId) -or $containerId.Contains([char]10)) {
            throw "Contêiner de $node indisponível ou ambíguo no Compose."
        }

        # Verifica a configuração REAL do contêiner que está rodando, não só
        # interpolação do YAML. Os valores completos podem conter senhas:
        # nunca os escreva no console ou no relatório.
        $inspection = (& docker inspect $containerId | Out-String)
        if ($LASTEXITCODE -ne 0) { throw "Docker inspect falhou para $node." }
        $details = @($inspection | ConvertFrom-Json)
        if ($details.Count -ne 1 -or -not $details[0].State.Running) {
            throw "Contêiner de $node não está em execução."
        }

        foreach ($prefix in @('ConnectionStrings__Jornada=','JORNADA_SQL_CONNECTION_STRING=')) {
            $entries = @($details[0].Config.Env | Where-Object {
                $_ -is [string] -and $_.StartsWith($prefix, [StringComparison]::Ordinal)
            })
            if ($entries.Count -ne 1) { throw "$node não declarou uma única configuração $prefix." }
            $connectionValue = ([string]$entries[0]).Substring($prefix.Length)
            $databaseMatch = [regex]::Match(
                $connectionValue, '(?i)(?:^|;)\s*(?:Database|Initial\s+Catalog)\s*=\s*([^;]+)')
            if (-not $databaseMatch.Success -or
                $databaseMatch.Groups[1].Value.Trim().Trim([char]34) -ne $DatabaseName) {
                throw "$node não está conectado ao banco original $DatabaseName."
            }
        }

        $containers[$node] = $containerId
        Write-Host "PASS: $node está ativo e usa o banco $DatabaseName."
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
        $response = Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 8
        if ([int]$response.StatusCode -ne 200) { throw "Health/ready falhou: $url" }
    }

    # Prova bidirecional do volume, sem shell remoto e fora da árvore sha256/.
    # docker cp funciona igualmente em Windows PowerShell 5.1 e PowerShell 7.
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    $seedFile = Join-Path $tempRoot 'from-node1.txt'
    $fromNode2 = Join-Path $tempRoot 'read-node2.txt'
    $responseFile = Join-Path $tempRoot 'from-node2.txt'
    $fromNode1 = Join-Path $tempRoot 'read-node1.txt'
    Set-Content -LiteralPath $seedFile -Encoding ASCII -NoNewline -Value $nonce
    & docker cp $seedFile "$($containers['jornada-node1']):/data/bronze/$marker"
    if ($LASTEXITCODE -ne 0) { throw 'NODE1 não conseguiu gravar o marcador efêmero no NAS.' }
    & docker cp "$($containers['jornada-node2']):/data/bronze/$marker" $fromNode2
    if ($LASTEXITCODE -ne 0 -or (Get-Content -LiteralPath $fromNode2 -Raw -Encoding ASCII) -cne $nonce) {
        throw 'NODE2 não conseguiu ler o marcador de NODE1 no NAS.'
    }

    Set-Content -LiteralPath $responseFile -Encoding ASCII -NoNewline -Value ($nonce + ':node2')
    & docker cp $responseFile "$($containers['jornada-node2']):/data/bronze/$marker"
    if ($LASTEXITCODE -ne 0) { throw 'NODE2 não conseguiu atualizar o marcador efêmero no NAS.' }
    & docker cp "$($containers['jornada-node1']):/data/bronze/$marker" $fromNode1
    if ($LASTEXITCODE -ne 0 -or
        (Get-Content -LiteralPath $fromNode1 -Raw -Encoding ASCII) -cne ($nonce + ':node2')) {
        throw 'NODE1 não conseguiu ler a alteração de NODE2 no NAS.'
    }

    Write-Host 'PASS: NODE1 e NODE2 usam JornadaLocal e compartilham o volume Bronze NAS. Nenhum dado SQL foi modificado.' -ForegroundColor Green
}
finally {
    foreach ($node in @('jornada-node1','jornada-node2')) {
        if ($containers.ContainsKey($node)) {
            & docker exec $containers[$node] rm -f "/data/bronze/$marker" *> $null
        }
    }
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    Pop-Location
}
