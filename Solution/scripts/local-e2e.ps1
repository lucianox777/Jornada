$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
$Example = Join-Path $Root '.env.example'
$ApiUrl = if ($env:JORNADA_E2E_API_URL) { $env:JORNADA_E2E_API_URL.TrimEnd('/') } else { 'http://127.0.0.1:5088' }
$Out = Join-Path $Root '.local/e2e'

foreach ($cmd in @('docker','dotnet','curl.exe','python')) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) { throw "Comando '$cmd' não encontrado no PATH." }
}
if (-not (Test-Path $EnvFile)) { Copy-Item $Example $EnvFile }
$vars = @{}
Get-Content $EnvFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
        $p = $line.Split('=',2); $vars[$p[0].Trim()] = $p[1]
    }
}
$password = $vars['JORNADA_SQL_SA_PASSWORD']; if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD não definido.' }
$port = if ($vars['JORNADA_SQL_PORT']) { $vars['JORNADA_SQL_PORT'] } else { '14333' }
$db = if ($vars['JORNADA_SQL_DATABASE']) { $vars['JORNADA_SQL_DATABASE'] } else { 'JornadaLocal' }

New-Item -ItemType Directory -Force $Out | Out-Null
foreach ($name in @('bronze','staging','packages')) {
    $path = Join-Path $Out $name
    if (Test-Path $path) { Remove-Item -Recurse -Force $path }
    New-Item -ItemType Directory -Force $path | Out-Null
}

& (Join-Path $PSScriptRoot 'local-db.ps1') -Action reset
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$conn = "Server=localhost,$port;Database=$db;User Id=sa;Password=$password;TrustServerCertificate=true;Encrypt=false"
$env:ConnectionStrings__Jornada = $conn
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = $ApiUrl
$env:BronzeStorage__Provider = 'FileSystem'
$env:BronzeStorage__RootPath = (Join-Path $Out 'bronze')
$env:IngestionStaging__RootPath = (Join-Path $Out 'staging')
$env:Processor__PollingMilliseconds = '100'

Push-Location $Root
try {
    dotnet restore Jornada.sln
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore falhou.' }
    dotnet build Jornada.sln --configuration Release --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build falhou.' }
} finally { Pop-Location }

$api = $null; $worker = $null
try {
    $api = Start-Process dotnet -WorkingDirectory $Root -ArgumentList @('run','--no-build','--configuration','Release','--no-launch-profile','--project','src/Jornada.Api') -RedirectStandardOutput (Join-Path $Out 'api.log') -RedirectStandardError (Join-Path $Out 'api.err.log') -PassThru
    $worker = Start-Process dotnet -WorkingDirectory $Root -ArgumentList @('run','--no-build','--configuration','Release','--project','src/Jornada.Processor.Worker') -RedirectStandardOutput (Join-Path $Out 'processor.log') -RedirectStandardError (Join-Path $Out 'processor.err.log') -PassThru

    $ready = $false
    for ($i=0; $i -lt 120; $i++) {
        $code = (& curl.exe -sS -o (Join-Path $Out 'ready.json') -w '%{http_code}' "$ApiUrl/health/ready" 2>$null | Out-String).Trim()
        if ($code -eq '200') { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw 'API não ficou ready.' }

    $package = (& python (Join-Path $Root 'scripts/build-ingestion-fixture.py') --fixture (Join-Path $Root 'tests/fixtures/ingestao/AA01_v2') --gestor SEHAB --output-dir (Join-Path $Out 'packages') | Out-String).Trim()
    if (-not (Test-Path $package)) { throw 'Fixture ZIP não foi gerada.' }
    $filename = Split-Path -Leaf $package
    $accessKey = 'KcUBZuLvRCu0lKN6xmXdjGKhPTgluG1Wu0sFB36lvTY'

    function Read-Json([string]$path) { return (Get-Content -Raw -Encoding UTF8 $path | ConvertFrom-Json) }
    function Post-Delivery([string]$idem,[string]$body,[string]$codeFile) {
        $args = @('-sS','-o',$body,'-w','%{http_code}','-X','POST',"$ApiUrl/api/v1/ingestao/entregas",
            '-H','X-Jornada-Gestor: SEHAB','-H',"X-Jornada-Access-Key: $accessKey",
            '-H',"Idempotency-Key: $idem",'-H','Content-Type: application/zip',
            '-H',"Content-Disposition: attachment; filename=$filename",'--data-binary',"@$package")
        $code = (& curl.exe @args | Out-String).Trim(); Set-Content -Encoding ascii $codeFile $code
    }
    function Wait-Processed([string]$id,[string]$outFile) {
        for ($i=0; $i -lt 120; $i++) {
            & curl.exe -sS -o $outFile "$ApiUrl/api/v1/ingestao/entregas/$id" -H 'X-Jornada-Gestor: SEHAB' -H "X-Jornada-Access-Key: $accessKey" | Out-Null
            $status = (Read-Json $outFile).status
            if ($status -eq 'PROCESSADA') { return }
            if ($status -in @('REJEITADA','QUARENTENA')) { throw "Entrega $id terminou $status." }
            Start-Sleep -Seconds 1
        }
        throw "Timeout aguardando Entrega $id."
    }
    function Sql([string]$query) {
        Push-Location $Root
        try {
            $lines = & docker compose --env-file $EnvFile exec -T -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $db -W -h -1 -Q "SET NOCOUNT ON; $query"
            if ($LASTEXITCODE -ne 0) { throw 'sqlcmd falhou.' }
            return @($lines | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        } finally { Pop-Location }
    }
    function Scalar([string]$query) { $lines = @(Sql $query); if ($lines.Count -eq 0) { return '' }; return $lines[-1].Replace(' ','') }

    $post1 = Join-Path $Out 'post1.json'; $post1Code = Join-Path $Out 'post1.code'
    Post-Delivery 'local-e2e-001' $post1 $post1Code
    if ((Get-Content -Raw $post1Code).Trim() -ne '202') { throw 'POST inicial não retornou 202.' }
    $id1 = (Read-Json $post1).entregaId; Wait-Processed $id1 (Join-Path $Out 'status1.json')

    $postReplay = Join-Path $Out 'post-idempotent.json'; $postReplayCode = Join-Path $Out 'post-idempotent.code'
    Post-Delivery 'local-e2e-001' $postReplay $postReplayCode
    if ((Get-Content -Raw $postReplayCode).Trim() -ne '202') { throw 'Replay da mesma Idempotency-Key não retornou 202.' }
    $idSame = (Read-Json $postReplay).entregaId
    if ($idSame -ne $id1) { throw 'Mesma Idempotency-Key criou outra Entrega.' }
    if ((Scalar "SELECT COUNT(*) FROM ingestao.entrega WHERE idempotency_key='local-e2e-001';") -ne '1') { throw 'Borda idempotente duplicou Entrega.' }

    if ((Scalar "SELECT COUNT(*) FROM bronze.entrega_arquivo WHERE entrega_id='$id1';") -ne '1') { throw 'Bronze não materializada.' }
    if ((Scalar "SELECT COUNT(*) FROM silver.pessoa_observacao po JOIN ingestao.lote l ON l.lote_id=po.lote_id WHERE l.entrega_id='$id1';") -ne '1') { throw 'Silver não materializada.' }
    if ((Scalar "SELECT COUNT(*) FROM gold.pessoa WHERE cpf='70819234532';") -ne '1') { throw 'Gold Pessoa ausente.' }
    if ((Scalar "SELECT COUNT(*) FROM serving.v_beneficios_concedidos_pessoa WHERE codigo_registro_origem='E2E-AA01-2026-000001';") -ne '1') { throw 'Serving factual ausente.' }

    $resolve = Join-Path $Out 'resolve.json'; $resolveCode = Join-Path $Out 'resolve.code'
    $code = (& curl.exe -sS -o $resolve -w '%{http_code}' -X POST "$ApiUrl/api/v1/identidade/resolver" -H 'Content-Type: application/json' -H 'X-Jornada-Gestor: SEHAB' -H "X-Jornada-Access-Key: $accessKey" --data '{"cpf":"70819234532"}' | Out-String).Trim()
    Set-Content -Encoding ascii $resolveCode $code; if ($code -ne '200') { throw 'Resolver API falhou.' }
    $pessoaUuid = (Read-Json $resolve).pessoaUuid; if ([string]::IsNullOrWhiteSpace($pessoaUuid)) { throw 'Resolver não retornou UUID.' }
    $code = (& curl.exe -sS -o (Join-Path $Out 'person.json') -w '%{http_code}' "$ApiUrl/api/v1/pessoas/$pessoaUuid" -H 'X-Jornada-Gestor: SEHAB' -H "X-Jornada-Access-Key: $accessKey" | Out-String).Trim()
    if ($code -ne '200') { throw 'Retorno da Pessoa pela API falhou.' }
    $records = Join-Path $Out 'records.json'
    $code = (& curl.exe -sS -o $records -w '%{http_code}' "$ApiUrl/api/v1/pessoas/$pessoaUuid/registros" -H 'X-Jornada-Gestor: SEHAB' -H "X-Jornada-Access-Key: $accessKey" | Out-String).Trim()
    if ($code -ne '200' -or -not ((Get-Content -Raw $records).Contains('E2E-AA01-2026-000001'))) { throw 'Registro esperado não voltou pela API.' }

    $post2 = Join-Path $Out 'post2.json'; $post2Code = Join-Path $Out 'post2.code'
    Post-Delivery 'local-e2e-002' $post2 $post2Code
    if ((Get-Content -Raw $post2Code).Trim() -ne '202') { throw 'Segunda Entrega não retornou 202.' }
    $id2 = (Read-Json $post2).entregaId; if ($id2 -eq $id1) { throw 'Nova Idempotency-Key não criou nova Entrega lógica.' }
    Wait-Processed $id2 (Join-Path $Out 'status2.json')
    $retrans = [int](Scalar "SELECT COUNT(*) FROM ingestao.item_processado ip JOIN ingestao.lote l ON l.lote_id=ip.lote_id WHERE l.entrega_id='$id2' AND ip.resultado='RETRANSMITIDO';")
    if ($retrans -lt 2) { throw "Retransmissão não foi reconhecida; itens=$retrans." }
    if ((Scalar "SELECT COUNT(*) FROM gold.beneficio_concedido WHERE codigo_registro_origem='E2E-AA01-2026-000001' AND status_analitico='VIGENTE';") -ne '1') { throw 'Retransmissão duplicou a versão Gold vigente.' }

    [ordered]@{
        status='OK'; generatedAtUtc=[DateTimeOffset]::UtcNow.ToString('O'); firstEntregaId=$id1; retransmissionEntregaId=$id2
        pessoaUuid=$pessoaUuid; idempotencyKeyReplaySameEntrega=$true; retransmittedItems=$retrans
        layers=@('HTTP','Bronze','Processor','Silver','Gold','Serving','HTTP-return')
    } | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 (Join-Path $Out 'evidence.json')
    Get-Content (Join-Path $Out 'evidence.json'); Write-Host 'LOCAL E2E: OK'
}
finally {
    if ($worker -and -not $worker.HasExited) { Stop-Process -Id $worker.Id -Force -ErrorAction SilentlyContinue }
    if ($api -and -not $api.HasExited) { Stop-Process -Id $api.Id -Force -ErrorAction SilentlyContinue }
}
