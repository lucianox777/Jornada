param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('system-status','reference-check','bronze-verify','ingest-latest','pipeline-status','blocking','calibrate-initial','calibrate','linkage','replay-latest','report')]
    [string]$Action,
    [string]$ZipPath
)

$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$OutputEncoding=[Text.UTF8Encoding]::new($false)
if($PSVersionTable.PSVersion.Major -ge 7){$PSStyle.OutputRendering='PlainText'}
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'dev-console-env.ps1')
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile=$DevConsoleEnvFile
$KeysFile=Join-Path $Root 'config/security/test-access-keys.json'
$OutDir=Join-Path $Root '.local/dev-console'
New-Item -ItemType Directory -Force $OutDir | Out-Null

if(-not(Test-Path $EnvFile)){throw '.env ausente. Suba a infraestrutura DEV primeiro.'}
$vars=@{}
Get-Content $EnvFile | ForEach-Object {
    $line=$_.Trim()
    if($line -and -not $line.StartsWith('#') -and $line.Contains('=')){
        $p=$line.Split('=',2)
        $vars[$p[0].Trim()]=$p[1].Trim()
    }
}
$db=if($vars['JORNADA_SQL_DATABASE']){$vars['JORNADA_SQL_DATABASE']}else{'JornadaLocal'}
$password=$vars['JORNADA_SQL_SA_PASSWORD']
if([string]::IsNullOrWhiteSpace($password)){throw 'JORNADA_SQL_SA_PASSWORD ausente.'}

function Invoke-Compose([Parameter(ValueFromRemainingArguments=$true)][string[]]$ComposeArgs){
    if($ComposeArgs.Count -eq 0){throw "Invoke-Compose exige um subcomando do Docker Compose."}
    Write-Host ('# docker compose --env-file .env '+($ComposeArgs -join ' '))
    Push-Location $Root
    try {
        & docker compose --env-file $EnvFile @ComposeArgs
        $composeExitCode=$LASTEXITCODE
        if($composeExitCode -ne 0){throw "docker compose falhou ($composeExitCode)."}
    } finally { Pop-Location }
}

function Invoke-SqlScalar([string]$Query){
    $old=$env:SQLCMDPASSWORD
    $env:SQLCMDPASSWORD=$password
    Push-Location $Root
    try {
        $raw=@(& docker compose --env-file $EnvFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -W -h -1 -Q "SET NOCOUNT ON; $Query")
        if($LASTEXITCODE -ne 0){throw "sqlcmd falhou ($LASTEXITCODE)."}
        return [string](@($raw | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ }) | Select-Object -Last 1)
    } finally {
        if($null -eq $old){Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue}else{$env:SQLCMDPASSWORD=$old}
        Pop-Location
    }
}

function Ensure-ClusterRunning {
    $required=@('sqlserver','jornada-nas','jornada-node1','jornada-node2')
    $running=@()
    try {
        Push-Location $Root
        $running=@(& docker compose --env-file $EnvFile ps --status running --services 2>$null)
        Pop-Location
    } catch {
        try { Pop-Location } catch {}
        $running=@()
    }
    $missing=@($required | Where-Object { $_ -notin $running })
    if($missing.Count -eq 0){ return }

    Write-Host "Infraestrutura incompleta ($($missing -join ', ')); subindo automaticamente..."
    & (Join-Path $PSScriptRoot 'dev-console-infrastructure.ps1') -Action up
    if($LASTEXITCODE -ne 0){throw "Subida automática da infraestrutura falhou ($LASTEXITCODE)."}
}

function Invoke-ClusterAction([string]$ClusterAction){
    Ensure-ClusterRunning
    Write-Host "# pwsh -NoProfile -File scripts/local-cluster.ps1 -Action $ClusterAction"
    & (Join-Path $PSScriptRoot 'local-cluster.ps1') -Action $ClusterAction
    if($LASTEXITCODE -ne 0){throw "local-cluster.ps1 $ClusterAction falhou ($LASTEXITCODE)."}
}

function Get-DevCredential([string]$Gestor,[string]$RequiredScope){
    if(-not(Test-Path $KeysFile)){throw "Credenciais DEV não encontradas: $KeysFile"}
    $keys=Get-Content $KeysFile -Raw | ConvertFrom-Json
    $credential=@($keys.credentials | Where-Object {
        $_.type -eq 'GESTOR' -and $_.publicCode -eq $Gestor -and @($_.scopes) -contains $RequiredScope
    } | Select-Object -First 1)
    if($credential.Count -ne 1){throw "Credencial DEV GESTOR $Gestor sem scope $RequiredScope."}
    return $credential[0]
}

switch($Action){
    'system-status' {
        Write-Host '=== ESTADO GERAL DO SISTEMA ==='
        Write-Host ''
        Write-Host '[1/5] Containers e health/readiness'
        & (Join-Path $PSScriptRoot 'dev-console-infrastructure.ps1') -Action status
        if($LASTEXITCODE -ne 0){throw "Status da infraestrutura falhou ($LASTEXITCODE)."}

        Write-Host ''
        Write-Host '[2/5] SQL/schema'
        $schemaCount=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM sys.schemas WHERE name IN (N'ingestao',N'bronze',N'silver',N'gold',N'identidade',N'ref');")
        Write-Host "Schemas essenciais presentes: $schemaCount/6"
        if($schemaCount -ne 6){throw "Schema incompleto: $schemaCount/6."}

        Write-Host ''
        Write-Host '[3/5] Referência IBGE'
        & (Join-Path $PSScriptRoot 'local-check-ibge-reference.ps1') -NoStart
        if($LASTEXITCODE -ne 0){throw "Quick check IBGE falhou ($LASTEXITCODE)."}

        Write-Host ''
        Write-Host '[4/5] Modelo de linkage'
        $active=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status=N'ATIVO' AND ISNULL(amostra_metodo,N'')<>N'SEED_DEV_FIXO_NAO_TREINADO';")
        Write-Host "Modelos calibrados ATIVOS: $active"
        if($active -ne 1){throw "Esperado exatamente 1 modelo calibrado ATIVO; atual=$active."}

        Write-Host ''
        Write-Host '[5/5] Processos residentes'
        Invoke-Compose @('exec','-T','jornada-node1','sh','-lc',"pgrep -af '[J]ornada.Processor.Worker.dll' >/dev/null")
        Invoke-Compose @('exec','-T','jornada-node2','sh','-lc',"pgrep -af '[J]ornada.Processor.Worker.dll' >/dev/null")
        Invoke-Compose @('exec','-T','jornada-node2','sh','-lc',"test -f /data/node/linkage-runner.enabled && test -s /data/node/linkage-runner-loop.pid && kill -0 \$(cat /data/node/linkage-runner-loop.pid)")
        Write-Host 'Processor: residente em NODE1/NODE2'
        Write-Host 'Linkage Runner: residente em loop incremental no NODE2'
        Write-Host ''
        Write-Host 'ESTADO GERAL: OK'
    }

    'reference-check' {
        Ensure-ClusterRunning
        Write-Host '# pwsh -NoProfile -File scripts/local-check-ibge-reference.ps1 -NoStart'
        & (Join-Path $PSScriptRoot 'local-check-ibge-reference.ps1') -NoStart
        if($LASTEXITCODE -ne 0){throw "Quick check IBGE falhou ($LASTEXITCODE)."}
    }

    'bronze-verify' {
        Ensure-ClusterRunning
        Invoke-Compose @('exec','-T','jornada-node2','dotnet','/opt/jornada/tools/Jornada.Bronze.Verify/Jornada.Bronze.Verify.dll','--minimum-count','0')
    }

    'ingest-latest' {
        Ensure-ClusterRunning
        $contractBundle=Join-Path $OutDir 'contract-config-bundle.zip'
        if(-not(Test-Path $contractBundle)){throw 'Bundle de contratos/configurações ausente. Execute primeiro Gerar bundle de contratos e configurações.'}
        Write-Host "Bundle de contrato validado: $contractBundle"
        $manualRoot=Join-Path $OutDir 'manual-zip'
        if(-not(Test-Path $manualRoot)){throw 'Nenhum ZIP manual foi gerado ainda.'}
        if([string]::IsNullOrWhiteSpace($ZipPath)){
            $zip=Get-ChildItem $manualRoot -Recurse -File -Filter '*.zip' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
            if($null -eq $zip){throw 'Nenhum ZIP manual foi gerado ainda.'}
        } else {
            $manualFull=[IO.Path]::GetFullPath($manualRoot)
            $requested=[IO.Path]::GetFullPath($ZipPath)
            $prefix=$manualFull.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
            if(-not $requested.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'ZipPath deve apontar para um ZIP gerado pela Console DEV.'}
            if(-not(Test-Path -LiteralPath $requested -PathType Leaf)){throw "ZIP informado não existe: $requested"}
            $zip=Get-Item -LiteralPath $requested
        }
        if($zip.Name -notmatch '^ENTREGA_([^_]+)_.+_v2_([0-9a-fA-F]{64})\.zip$'){throw "Nome de ZIP não canônico: $($zip.Name)"}
        $gestor=$Matches[1]
        $sha=$Matches[2].ToLowerInvariant()
        $credential=Get-DevCredential $gestor 'jornada.ingestao.write'
        $uri='http://127.0.0.1:5080/api/v1/ingestao/entregas'
        Write-Host "# POST $uri"
        Write-Host "ZIP: $($zip.FullName)"
        Write-Host "Gestor: $gestor"
        $headers=@{
            'X-Jornada-Gestor'=$gestor
            'X-Jornada-Access-Key'=[string]$credential.accessKey
            'Idempotency-Key'="sha256:$sha"
            'Content-Disposition'="attachment; filename=$($zip.Name)"
        }
        $response=Invoke-WebRequest -UseBasicParsing -Method Post -Uri $uri -Headers $headers -ContentType 'application/zip' -InFile $zip.FullName
        Write-Host "HTTP $([int]$response.StatusCode)"
        Write-Host $response.Content
        $receipt=$response.Content | ConvertFrom-Json
        $envelope=[ordered]@{
            zip=$zip.FullName
            gestor=$gestor
            database=$db
            receivedAt=(Get-Date).ToUniversalTime().ToString('o')
            receipt=$receipt
        }
        $result=Join-Path $OutDir 'last-ingestion.json'
        $envelope | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 $result
        Write-Host "Resultado salvo em: $result"
        Write-Host "ARTEFATO: $result"
    }

    'pipeline-status' {
        Ensure-ClusterRunning
        $result=Join-Path $OutDir 'last-ingestion.json'
        if(-not(Test-Path $result)){throw 'Nenhuma ingestão registrada pela Console DEV.'}
        $saved=Get-Content $result -Raw | ConvertFrom-Json
        $entregaId=[string]$saved.receipt.entregaId
        if([string]::IsNullOrWhiteSpace($entregaId)){$entregaId=[string]$saved.receipt.EntregaId}
        if([string]::IsNullOrWhiteSpace($entregaId)){throw 'Recibo da última ingestão não contém entregaId.'}
        $gestor=[string]$saved.gestor
        $savedDb=[string]$saved.database
        $statusPath=Join-Path $OutDir 'last-ingestion-status.json'
        $exists=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM ingestao.entrega WHERE entrega_id='$entregaId';")
        if(($savedDb -and $savedDb -ne $db) -or $exists -eq 0){
            $stale=[ordered]@{
                entregaId=$entregaId
                gestor=$gestor
                databaseAtual=$db
                databaseDoRecibo=$savedDb
                status='NAO_ENCONTRADA_NO_AMBIENTE_ATUAL'
                staleReceipt=$true
                message='O recibo pertence a uma execução anterior/ambiente reinicializado. Envie um novo ZIP antes de consultar o status.'
                checkedAt=(Get-Date).ToUniversalTime().ToString('o')
            }
            $stale | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 $statusPath
            Write-Host "Recibo anterior detectado: entrega $entregaId não existe no banco atual $db."
            Write-Host 'Nenhuma chamada HTTP foi feita para evitar um 404 enganoso.'
            Write-Host "Resultado salvo em: $statusPath"
            Write-Host "ARTEFATO: $statusPath"
            return
        }

        $credential=Get-DevCredential $gestor 'jornada.ingestao.status'
        $uri="http://127.0.0.1:5080/api/v1/ingestao/entregas/$entregaId"
        Write-Host "# GET $uri"
        $headers=@{
            'X-Jornada-Gestor'=$gestor
            'X-Jornada-Access-Key'=[string]$credential.accessKey
        }
        $response=Invoke-WebRequest -UseBasicParsing -Method Get -Uri $uri -Headers $headers
        Write-Host "HTTP $([int]$response.StatusCode)"
        Write-Host $response.Content
        $response.Content | Set-Content -Encoding UTF8 $statusPath
        Write-Host "Resultado salvo em: $statusPath"
        Write-Host "ARTEFATO: $statusPath"
        $pipeline=$response.Content | ConvertFrom-Json
        $pipelineStatus=[string]$pipeline.status
        if($pipelineStatus -in @('QUARENTENA','REJEITADA','POISON','FALHA')){
            $pipelineError=[string]$pipeline.erro
            throw "Ingestão terminou em $pipelineStatus$(if($pipelineError){": $pipelineError"}). Consulte $statusPath."
        }
    }

    'blocking' { Invoke-ClusterAction 'blocking' }

    'calibrate-initial' {
        Ensure-ClusterRunning
        if($db -ne 'JornadaSyntheticDev'){throw "Console DEV exige JornadaSyntheticDev; banco atual=$db."}
        $eligible="status=N'ATIVO' AND ISNULL(amostra_metodo,N'')<>N'SEED_DEV_FIXO_NAO_TREINADO'"
        $activeCount=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE $eligible;")
        if($activeCount -gt 1){throw "Estado inválido: encontrados $activeCount modelos calibrados ATIVOS."}
        $reused=($activeCount -eq 1)

        if(-not $reused){
            $goldCount=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(DISTINCT vc.pessoa_uuid) FROM silver.pessoa_observacao o JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=o.pessoa_observacao_id WHERE o.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%' AND vc.status=N'RESOLVIDO';")
            $goldProfile=Join-Path $OutDir 'gold-synthetic-profile.json'
            if($goldCount -ne 30000 -or -not(Test-Path $goldProfile)){
                Write-Host "Modelo BOOTSTRAP ainda não existe. Materializando a Gold sintética canônica de 30.000 pessoas antes da calibração..."
                & (Join-Path $PSScriptRoot 'dev-console-gold-synthetic.ps1')
                if($LASTEXITCODE -ne 0){throw "Carga da Gold sintética falhou ($LASTEXITCODE)."}
                $goldCount=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(DISTINCT vc.pessoa_uuid) FROM silver.pessoa_observacao o JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=o.pessoa_observacao_id WHERE o.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%' AND vc.status=N'RESOLVIDO';")
            }
            if($goldCount -ne 30000){throw "Modelo BOOTSTRAP exige a Gold sintética completa de 30.000 pessoas; atual=$goldCount."}

            Write-Host "Gerando o modelo BOOTSTRAP inicial a partir da referência IBGE + Gold sintética DEV ($goldCount pessoas)..."
            Invoke-ClusterAction 'calibrate'
            $activeCount=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE $eligible;")
            if($activeCount -ne 1){throw "Calibração inicial deveria deixar exatamente 1 modelo BOOTSTRAP ATIVO; atual=$activeCount."}
        } else {
            Write-Host 'Modelo calibrado ATIVO já existe; preservando a versão corrente sem recalibrar.'
        }

        $goldCount=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(DISTINCT vc.pessoa_uuid) FROM silver.pessoa_observacao o JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=o.pessoa_observacao_id WHERE o.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%' AND vc.status=N'RESOLVIDO';")
        $modelId=Invoke-SqlScalar "SELECT TOP(1) CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE $eligible ORDER BY versao DESC;"
        $version=[int](Invoke-SqlScalar "SELECT TOP(1) versao FROM identidade.modelo_linkage WHERE $eligible ORDER BY versao DESC;")
        $bundleVersion=Invoke-SqlScalar "SELECT TOP(1) ISNULL(model_config_bundle_version,N'') FROM identidade.modelo_linkage WHERE $eligible ORDER BY versao DESC;"
        $fingerprint=Invoke-SqlScalar "SELECT TOP(1) ISNULL(model_config_bundle_fingerprint_sha256,N'') FROM identidade.modelo_linkage WHERE $eligible ORDER BY versao DESC;"
        $sampleMethod=Invoke-SqlScalar "SELECT TOP(1) ISNULL(amostra_metodo,N'') FROM identidade.modelo_linkage WHERE $eligible ORDER BY versao DESC;"
        $result=[ordered]@{
            generatedAt=(Get-Date).ToUniversalTime().ToString('o')
            mode=if($reused){'REUSED_ACTIVE'}else{'CREATED_AND_ACTIVATED_BOOTSTRAP'}
            modelRole=if($reused){'ACTIVE_CURRENT'}else{'BOOTSTRAP'}
            bootstrapReference='IBGE_CENSO_2022'
            goldPeople=$goldCount
            modelId=$modelId
            version=$version
            status='ATIVO'
            sampleMethod=$sampleMethod
            modelConfigBundleVersion=$bundleVersion
            modelConfigBundleFingerprintSha256=$fingerprint
        }
        $resultPath=Join-Path $OutDir 'initial-calibration.json'
        $result | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 $resultPath
        if($reused){Write-Host "Modelo ATIVO preservado: v$version ($modelId)."}
        else{Write-Host "Modelo BOOTSTRAP inicial ATIVO: v$version ($modelId)."}
        Write-Host "Resultado salvo em: $resultPath"
        Write-Host "ARTEFATO: $resultPath"
    }

    'calibrate' { Invoke-ClusterAction 'calibrate' }

    'linkage' {
        Ensure-ClusterRunning
        $eligibleActive=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status=N'ATIVO' AND ISNULL(amostra_metodo,N'')<>N'SEED_DEV_FIXO_NAO_TREINADO';")
        if($eligibleActive -ne 1){
            throw "Executar linkage exige exatamente 1 modelo ATIVO; atual=$eligibleActive. A subida da infraestrutura deve garantir o BOOTSTRAP inicial (IBGE + corpus sintético). Execute novamente 'Subir infraestrutura, referências e bootstrap' para reparar/confirmar o estado. O seed fixo não libera linkage."
        }
        Invoke-ClusterAction 'linkage'
    }

    'replay-latest' {
        Ensure-ClusterRunning
        $sourceRun=Invoke-SqlScalar "SELECT TOP(1) CONVERT(varchar(36),linkage_run_id) FROM identidade.linkage_run WHERE status=N'PUBLICADO' AND tipo_run<>N'REPLAY' ORDER BY publicado_em DESC,iniciado_em DESC;"
        if([string]::IsNullOrWhiteSpace($sourceRun)){throw 'Nenhum linkage PUBLICADO elegível para replay.'}
        Write-Host "Replay histórico do último run publicado: $sourceRun"
        Invoke-Compose @('exec','-T','jornada-node2','dotnet','/opt/jornada/apps/Jornada.Linkage.Runner/Jornada.Linkage.Runner.dll','--mode','REPLAY','--replay-source-run-id',$sourceRun,'--requested-by','DEV_CONSOLE','--reason','manual-dev-console-replay','--publish','false')
    }

    'report' { Invoke-ClusterAction 'linkage-diagnose' }
}
