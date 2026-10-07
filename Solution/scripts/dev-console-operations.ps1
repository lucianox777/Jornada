param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('system-status','reference-check','bronze-verify','bronze-verify-latest','ingest-latest','pipeline-status','process-latest','blocking','bootstrap-corpus','calibrate-initial','calibrate','linkage','replay-latest','report')]
    [string]$Action,
    [string]$ZipPath
)

$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$OutputEncoding=[Text.UTF8Encoding]::new($false)
if($PSVersionTable.PSVersion.Major -ge 7){$PSStyle.OutputRendering='PlainText'}
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$existingConsoleEnv=Join-Path $Root '.env.devconsole'
if([string]::IsNullOrWhiteSpace($env:JORNADA_RUNTIME_MODE) -and (Test-Path $existingConsoleEnv)){
    $existingModeLine=Get-Content $existingConsoleEnv -Encoding UTF8 | Where-Object { $_ -match '^JORNADA_RUNTIME_MODE=' } | Select-Object -First 1
    if($existingModeLine){
        $existingMode=$existingModeLine.Split('=',2)[1].Trim().ToUpperInvariant()
        if($existingMode -in @('HML','DEV','PROD')){
            $env:JORNADA_RUNTIME_MODE=$existingMode
            Write-Host "RuntimeMode não informado; preservando modo ativo da Console: $existingMode."
        }
    }
}
. (Join-Path $PSScriptRoot 'dev-console-env.ps1')
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile=$DevConsoleEnvFile
$RuntimeMode=$DevConsoleRuntimeMode
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
$profile=if($vars['JORNADA_LOCAL_PROFILE']){$vars['JORNADA_LOCAL_PROFILE']}else{'dev-console'}
$password=$vars['JORNADA_SQL_SA_PASSWORD']
if([string]::IsNullOrWhiteSpace($password)){throw 'JORNADA_SQL_SA_PASSWORD ausente.'}

function Invoke-Compose([Parameter(ValueFromRemainingArguments=$true)][string[]]$ComposeArgs){
    if($ComposeArgs.Count -eq 0){throw "Invoke-Compose exige um subcomando do Docker Compose."}
    Write-Host ("# docker compose --env-file $(Split-Path -Leaf $EnvFile) "+($ComposeArgs -join ' '))
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
        $value=[string](@($raw | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ }) | Select-Object -Last 1)
        if($value -eq 'NULL'){return ''}
        return $value
    } finally {
        if($null -eq $old){Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue}else{$env:SQLCMDPASSWORD=$old}
        Pop-Location
    }
}

function Get-NodeProfileDrift {
    $drift=@()
    foreach($service in @('jornada-node1','jornada-node2')){
        $cid=(& docker compose --env-file $EnvFile ps -q $service 2>$null | Out-String).Trim()
        if([string]::IsNullOrWhiteSpace($cid)){continue}
        $actualProfile=(& docker inspect --format '{{ index .Config.Labels "com.jornada.local.profile" }}' $cid 2>$null | Out-String).Trim()
        $actualDatabase=(& docker inspect --format '{{ index .Config.Labels "com.jornada.local.database" }}' $cid 2>$null | Out-String).Trim()
        if($actualProfile -ne $profile -or $actualDatabase -ne $db){
            $drift+=("$service(profile=$actualProfile,database=$actualDatabase)")
        }
    }
    return @($drift)
}

function Ensure-ClusterRunning {
    $required=@('sqlserver','jornada-nas','jornada-node1','jornada-node2')
    $running=@()
    try {
        Push-Location $Root
        $running=@(& docker compose --env-file $EnvFile ps --status running --services 2>$null)
        $drift=@(Get-NodeProfileDrift)
        Pop-Location
    } catch {
        try { Pop-Location } catch {}
        $running=@()
        $drift=@()
    }
    $missing=@($required | Where-Object { $_ -notin $running })
    if($missing.Count -eq 0 -and $drift.Count -eq 0){ return }

    if($drift.Count -gt 0){
        Write-Host "Perfil dos nós divergente do ambiente $RuntimeMode esperado ($profile/$db): $($drift -join '; '). Reconciliando automaticamente..."
    }else{
        Write-Host "Infraestrutura incompleta ($($missing -join ', ')); subindo automaticamente..."
    }
    & (Join-Path $PSScriptRoot 'dev-console-infrastructure.ps1') -Action up
    if($LASTEXITCODE -ne 0){throw "Subida automática da infraestrutura falhou ($LASTEXITCODE)."}

    Push-Location $Root
    try {
        $running=@(& docker compose --env-file $EnvFile ps --status running --services 2>$null)
        $missing=@($required | Where-Object { $_ -notin $running })
        $drift=@(Get-NodeProfileDrift)
    } finally { Pop-Location }
    if($missing.Count -gt 0 -or $drift.Count -gt 0){
        throw "Infraestrutura $RuntimeMode não convergiu para o perfil esperado $profile/$db; ausentes=$($missing -join ','); divergentes=$($drift -join ';')."
    }
}

function Invoke-ClusterAction([string]$ClusterAction){
    Ensure-ClusterRunning
    $envName=Split-Path -Leaf $EnvFile
    Write-Host "# pwsh -NoProfile -File scripts/local-cluster.ps1 -Action $ClusterAction -EnvFile $envName -RuntimeMode $RuntimeMode"
    & (Join-Path $PSScriptRoot 'local-cluster.ps1') -Action $ClusterAction -EnvFile $EnvFile -RuntimeMode $RuntimeMode
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

function Ensure-BootstrapCorpus {
    Ensure-ClusterRunning
    $goldCount=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(DISTINCT vc.pessoa_uuid) FROM silver.pessoa_observacao o JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=o.pessoa_observacao_id WHERE o.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%' AND vc.status=N'RESOLVIDO';")
    $placeholderCount=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM gold.pessoa g WHERE g.nome_completo LIKE N'Pessoa Teste %' AND EXISTS(SELECT 1 FROM silver.pessoa_observacao o JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=o.pessoa_observacao_id WHERE o.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%' AND vc.pessoa_uuid=g.pessoa_uuid AND vc.status=N'RESOLVIDO');")
    $goldProfile=Join-Path $OutDir 'gold-synthetic-profile.json'
    if($goldCount -ne 30000 -or $placeholderCount -gt 0 -or -not(Test-Path $goldProfile)){
        Write-Host "Materializando a Gold sintética canônica de 30.000 pessoas; gold=$goldCount placeholders=$placeholderCount perfil=$([bool](Test-Path $goldProfile))..."
        & (Join-Path $PSScriptRoot 'dev-console-gold-synthetic.ps1')
        if($LASTEXITCODE -ne 0){throw "Carga da Gold sintética falhou ($LASTEXITCODE)."}
        $goldCount=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(DISTINCT vc.pessoa_uuid) FROM silver.pessoa_observacao o JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=o.pessoa_observacao_id WHERE o.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%' AND vc.status=N'RESOLVIDO';")
        $placeholderCount=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM gold.pessoa g WHERE g.nome_completo LIKE N'Pessoa Teste %' AND EXISTS(SELECT 1 FROM silver.pessoa_observacao o JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=o.pessoa_observacao_id WHERE o.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%' AND vc.pessoa_uuid=g.pessoa_uuid AND vc.status=N'RESOLVIDO');")
    }else{
        Write-Host 'Corpus sintético canônico já está materializado; preservando a Gold corrente.'
    }
    if($goldCount -ne 30000){throw "Corpus de calibração exige 30.000 pessoas Gold sintéticas; atual=$goldCount."}
    if($placeholderCount -ne 0){throw "Corpus de calibração ainda contém $placeholderCount nomes-placeholder após materialização."}

    $pending=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM silver.pessoa_observacao o WHERE o.codigo_pessoa_origem LIKE N'SCALE-PEND-%' AND NOT EXISTS(SELECT 1 FROM identidade.v_vinculo_corrente vc WHERE vc.pessoa_observacao_id=o.pessoa_observacao_id AND vc.status=N'RESOLVIDO');")
    $result=[ordered]@{
        generatedAt=(Get-Date).ToUniversalTime().ToString('o')
        runtimeMode=$RuntimeMode
        database=$db
        goldPeople=$goldCount
        pendingAdditional=$pending
        profile=$goldProfile
    }
    $resultPath=Join-Path $OutDir 'bootstrap-corpus.json'
    [IO.File]::WriteAllText($resultPath,($result|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
    Write-Host "Corpus de calibração pronto: Gold=$goldCount; adicionais pendentes=$pending."
    Write-Host "ARTEFATO: $resultPath"
    return $goldCount
}

function Get-DevBootstrapLinkageReadiness([string]$ModelId,[int]$ModelVersion){
    if([string]::IsNullOrWhiteSpace($ModelId)){throw 'Modelo ativo ausente ao avaliar readiness do bootstrap DEV.'}

    $total=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM silver.pessoa_observacao WHERE codigo_pessoa_origem LIKE N'SCALE-PEND-%';")
    $unresolved=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM silver.pessoa_observacao o WHERE o.codigo_pessoa_origem LIKE N'SCALE-PEND-%' AND NOT EXISTS(SELECT 1 FROM identidade.v_vinculo_corrente vc WHERE vc.pessoa_observacao_id=o.pessoa_observacao_id AND vc.status=N'RESOLVIDO');")
    $unevaluated=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM silver.pessoa_observacao o WHERE o.codigo_pessoa_origem LIKE N'SCALE-PEND-%' AND NOT EXISTS(SELECT 1 FROM identidade.v_vinculo_corrente vc WHERE vc.pessoa_observacao_id=o.pessoa_observacao_id AND vc.status=N'RESOLVIDO') AND NOT EXISTS(SELECT 1 FROM identidade.linkage_resultado r JOIN identidade.linkage_run lr ON lr.linkage_run_id=r.linkage_run_id WHERE r.pessoa_observacao_id=o.pessoa_observacao_id AND r.modelo_id='$ModelId' AND lr.status=N'PUBLICADO');")
    $associatedExisting=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM silver.pessoa_observacao o JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=o.pessoa_observacao_id AND vc.status=N'RESOLVIDO' JOIN identidade.linkage_resultado r ON r.linkage_run_id=vc.linkage_run_id AND r.pessoa_observacao_id=o.pessoa_observacao_id WHERE o.codigo_pessoa_origem LIKE N'SCALE-PEND-%' AND r.resultado_publicacao=N'ASSOCIACAO_EXISTENTE' AND r.status_publicacao=N'RESOLVIDO';")
    $newIdentity=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM silver.pessoa_observacao o JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=o.pessoa_observacao_id AND vc.status=N'RESOLVIDO' JOIN identidade.linkage_resultado r ON r.linkage_run_id=vc.linkage_run_id AND r.pessoa_observacao_id=o.pessoa_observacao_id WHERE o.codigo_pessoa_origem LIKE N'SCALE-PEND-%' AND r.resultado_publicacao=N'NOVA_IDENTIDADE' AND r.status_publicacao=N'RESOLVIDO';")
    $unexpectedNewIdentity=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM silver.pessoa_observacao o JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=o.pessoa_observacao_id AND vc.status=N'RESOLVIDO' JOIN identidade.linkage_resultado r ON r.linkage_run_id=vc.linkage_run_id AND r.pessoa_observacao_id=o.pessoa_observacao_id WHERE o.codigo_pessoa_origem LIKE N'SCALE-PEND-%' AND r.resultado_publicacao=N'NOVA_IDENTIDADE' AND r.status_publicacao=N'RESOLVIDO' AND TRY_CONVERT(bigint,RIGHT(o.codigo_pessoa_origem,10))%10<>0;")
    $falsePositiveAssociation=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM silver.pessoa_observacao o JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=o.pessoa_observacao_id AND vc.status=N'RESOLVIDO' JOIN identidade.linkage_resultado r ON r.linkage_run_id=vc.linkage_run_id AND r.pessoa_observacao_id=o.pessoa_observacao_id JOIN silver.pessoa_observacao truth_o ON truth_o.codigo_pessoa_origem=REPLACE(o.codigo_pessoa_origem,N'SCALE-PEND-',N'SCALE-SEHAB-') JOIN ref.gestor truth_g ON truth_g.gestor_id=truth_o.gestor_id AND truth_g.codigo=N'SEHAB' JOIN identidade.v_vinculo_corrente truth_vc ON truth_vc.pessoa_observacao_id=truth_o.pessoa_observacao_id AND truth_vc.status=N'RESOLVIDO' WHERE o.codigo_pessoa_origem LIKE N'SCALE-PEND-%' AND r.resultado_publicacao=N'ASSOCIACAO_EXISTENTE' AND r.status_publicacao=N'RESOLVIDO' AND r.pessoa_uuid_publicado<>truth_vc.pessoa_uuid;")
    $latestRunId=Invoke-SqlScalar "SELECT TOP(1) CONVERT(varchar(36),linkage_run_id) FROM identidade.linkage_run WHERE status=N'PUBLICADO' AND tipo_run=N'ON_DEMAND' AND modelo_id='$ModelId' ORDER BY publicado_em DESC,iniciado_em DESC,linkage_run_id DESC;"
    $threshold=Invoke-SqlScalar "SELECT CONVERT(varchar(40),valor) FROM identidade.parametro_linkage WHERE modelo_id='$ModelId' AND nome=N'T_LINKAGE';"
    $maxScore=$null
    $dominantReason=$null
    if(-not [string]::IsNullOrWhiteSpace($latestRunId)){
        $maxScore=Invoke-SqlScalar "SELECT CONVERT(varchar(40),MAX(r.score_melhor)) FROM identidade.linkage_resultado r JOIN silver.pessoa_observacao o ON o.pessoa_observacao_id=r.pessoa_observacao_id WHERE r.linkage_run_id='$latestRunId' AND o.codigo_pessoa_origem LIKE N'SCALE-PEND-%';"
        $dominantReason=Invoke-SqlScalar "SELECT TOP(1) CONCAT(COALESCE(r.motivo,N'SEM_MOTIVO'),N'|',COUNT_BIG(*)) FROM identidade.linkage_resultado r JOIN silver.pessoa_observacao o ON o.pessoa_observacao_id=r.pessoa_observacao_id WHERE r.linkage_run_id='$latestRunId' AND o.codigo_pessoa_origem LIKE N'SCALE-PEND-%' AND r.status<>N'RESOLVIDO' GROUP BY r.motivo ORDER BY COUNT_BIG(*) DESC,COALESCE(r.motivo,N'SEM_MOTIVO');"
    }

    $state=[ordered]@{
        generatedAt=(Get-Date).ToUniversalTime().ToString('o')
        modelId=$ModelId
        modelVersion=$ModelVersion
        totalAdditional=$total
        resolvedAdditional=($total-$unresolved)
        inconclusiveAdditional=$unresolved
        unevaluatedByActiveModel=$unevaluated
        associatedExistingAdditional=$associatedExisting
        newIdentityAdditional=$newIdentity
        unexpectedNewIdentityAdditional=$unexpectedNewIdentity
        falsePositiveAssociation=$falsePositiveAssociation
        latestPublishedOnDemandRunId=$latestRunId
        tLinkage=$threshold
        maxScoreLatestRun=$maxScore
        dominantInconclusiveReason=$dominantReason
        readinessDefinition='ZERO_UNEVALUATED_ZERO_FALSE_ASSOCIATION_ZERO_UNEXPECTED_NEW_IDENTITY; INCONCLUSIVE_AND_EXPECTED_NEW_IDENTITY_ARE_VALID_OUTCOMES'
    }
    $path=Join-Path $OutDir 'bootstrap-linkage-readiness.json'
    [IO.File]::WriteAllText($path,($state|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
    Write-Host "Readiness probabilístico DEV: total=$total; associações existentes=$associatedExisting; novas identidades=$newIdentity; inconclusivos=$unresolved; sem avaliação do modelo ativo=$unevaluated; falsas associações=$falsePositiveAssociation; novas identidades fora da coorte esperada=$unexpectedNewIdentity."
    if(-not [string]::IsNullOrWhiteSpace($threshold)){Write-Host "Fronteira observada: T_LINKAGE=$threshold; max_score_ultimo_run=$maxScore; motivo_inconclusivo_dominante=$dominantReason."}
    Write-Host "ARTEFATO: $path"
    return [pscustomobject]$state
}

switch($Action){
    'system-status' {
        Write-Host '=== ESTADO GERAL DO SISTEMA ==='
        Write-Host '[1/7] Status/health da infraestrutura (serviços Docker, containers/readiness e init one-shot de referência)'
        & (Join-Path $PSScriptRoot 'dev-console-infrastructure.ps1') -Action status
        if($LASTEXITCODE -ne 0){throw "Status da infraestrutura falhou ($LASTEXITCODE)."}

        Write-Host '[2/7] SQL/schema'
        $schemaCount=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM sys.schemas WHERE name IN (N'ingestao',N'bronze',N'silver',N'gold',N'identidade',N'ref');")
        Write-Host "Schemas essenciais presentes: $schemaCount/6"
        if($schemaCount -ne 6){throw "Schema incompleto: $schemaCount/6."}

        Write-Host '[3/7] Referência IBGE'
        & (Join-Path $PSScriptRoot 'local-check-ibge-reference.ps1') -NoStart
        if($LASTEXITCODE -ne 0){throw "Quick check IBGE falhou ($LASTEXITCODE)."}

        Write-Host '[4/7] Modelo de linkage'
        $active=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status=N'ATIVO' AND ISNULL(amostra_metodo,N'')<>N'SEED_DEV_FIXO_NAO_TREINADO';")
        Write-Host "Modelos calibrados ATIVOS: $active"

        Write-Host '[5/7] Processos e runners'
        $manualProcessor=([string]$vars['JORNADA_DEV_CONSOLE_MANUAL_PROCESSOR']).ToLowerInvariant() -eq 'true'
        if($manualProcessor){
            Invoke-Compose @('exec','-T','jornada-node2','sh','-lc',"test -f /opt/jornada/apps/Jornada.Processor.Worker/Jornada.Processor.Worker.dll")
            Write-Host 'Processor: execução manual one-shot no fluxo da Console DEV (ausência residente é esperada)'
        }else{
            Invoke-Compose @('exec','-T','jornada-node1','sh','-lc',"pgrep -af '[J]ornada.Processor.Worker.dll' >/dev/null")
            Invoke-Compose @('exec','-T','jornada-node2','sh','-lc',"pgrep -af '[J]ornada.Processor.Worker.dll' >/dev/null")
            Write-Host 'Processor: residente em NODE1/NODE2'
        }
        Invoke-Compose @('exec','-T','jornada-node2','sh','-lc',"test -f /opt/jornada/apps/Jornada.Linkage.Runner/Jornada.Linkage.Runner.dll && test -f /opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll")
        $residentRunner=(& docker compose --env-file $EnvFile exec -T jornada-node2 sh -lc "pgrep -af '[J]ornada.Linkage.Runner.dll' || true" | Out-String).Trim()
        Write-Host 'Runners de calibração/linkage: disponíveis no NODE2 (execução one-shot)'
        if([string]::IsNullOrWhiteSpace($residentRunner)){Write-Host 'Linkage Runner residente: não (esperado)'}else{Write-Host "Linkage Runner em execução neste instante: $residentRunner"}

        Write-Host '[6/7] Bronze'
        Invoke-Compose @('exec','-T','jornada-node2','dotnet','/opt/jornada/tools/Jornada.Bronze.Verify/Jornada.Bronze.Verify.dll','--minimum-count','0')

        Write-Host '[7/7] Último linkage'
        $publishedOnDemand=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM identidade.linkage_run lr JOIN identidade.modelo_linkage m ON m.modelo_id=lr.modelo_id WHERE lr.status=N'PUBLICADO' AND lr.tipo_run=N'ON_DEMAND' AND m.status=N'ATIVO' AND ISNULL(m.amostra_metodo,N'')<>N'SEED_DEV_FIXO_NAO_TREINADO';")
        if($publishedOnDemand -gt 0){
            Invoke-ClusterAction 'linkage-diagnose'
        }else{
            Write-Host 'Diagnóstico do último linkage: NÃO APLICÁVEL (nenhum run ON_DEMAND PUBLICADO para o modelo ATIVO).'
        }

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

    'bronze-verify-latest' {
        Ensure-ClusterRunning
        $last=Join-Path $OutDir 'last-ingestion.json'
        if(-not(Test-Path $last)){throw 'Nenhuma ingestão registrada pela Console DEV.'}
        $saved=Get-Content $last -Raw | ConvertFrom-Json
        $entregaId=[string]$saved.receipt.entregaId
        if([string]::IsNullOrWhiteSpace($entregaId)){$entregaId=[string]$saved.receipt.EntregaId}
        if([string]::IsNullOrWhiteSpace($entregaId)){throw 'Recibo da última ingestão não contém entregaId.'}
        $savedDb=[string]$saved.database
        $exists=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM ingestao.entrega WHERE entrega_id='$entregaId';")
        if(($savedDb -and $savedDb -ne $db) -or $exists -eq 0){throw 'A última ingestão não pertence ao ambiente DEV atual.'}

        $containerReport="/tmp/jornada-bronze-verify-$entregaId.json"
        $hostReport='.local/dev-console/last-bronze-verify.json'
        Write-Host "Verificando objeto Bronze da Entrega $entregaId..."
        Invoke-Compose @('exec','-T','jornada-node2','dotnet','/opt/jornada/tools/Jornada.Bronze.Verify/Jornada.Bronze.Verify.dll','--entrega-id',$entregaId,'--minimum-count','1','--report',$containerReport)
        Invoke-Compose @('cp',"jornada-node2:$containerReport",$hostReport)
        Invoke-Compose @('exec','-T','jornada-node2','rm','-f',$containerReport)
        $report=Join-Path $Root $hostReport
        if(-not(Test-Path $report)){throw "Relatório Bronze não foi copiado para $report."}
        Write-Host "Integridade Bronze da Entrega ${entregaId}: PASS"
        Write-Host "ARTEFATO: $report"
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

    'process-latest' {
        Ensure-ClusterRunning
        $manualProcessor=([string]$vars['JORNADA_DEV_CONSOLE_MANUAL_PROCESSOR']).ToLowerInvariant() -eq 'true'
        if(-not $manualProcessor){throw 'O fluxo didático exige JORNADA_DEV_CONSOLE_MANUAL_PROCESSOR=true. Suba a infraestrutura pela Console DEV antes de processar manualmente.'}

        $last=Join-Path $OutDir 'last-ingestion.json'
        if(-not(Test-Path $last)){throw 'Nenhuma ingestão registrada pela Console DEV.'}
        $saved=Get-Content $last -Raw | ConvertFrom-Json
        $entregaId=[string]$saved.receipt.entregaId
        if([string]::IsNullOrWhiteSpace($entregaId)){$entregaId=[string]$saved.receipt.EntregaId}
        if([string]::IsNullOrWhiteSpace($entregaId)){throw 'Recibo da última ingestão não contém entregaId.'}
        $savedDb=[string]$saved.database
        $exists=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM ingestao.entrega WHERE entrega_id='$entregaId';")
        if(($savedDb -and $savedDb -ne $db) -or $exists -eq 0){throw 'A última ingestão não pertence ao ambiente DEV atual.'}

        $status=Invoke-SqlScalar "SELECT status FROM ingestao.entrega WHERE entrega_id='$entregaId';"
        if($status -eq 'PROCESSADA'){
            $silver=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM silver.pessoa_observacao po JOIN ingestao.lote l ON l.lote_id=po.lote_id WHERE l.entrega_id='$entregaId';")
            Write-Host "Entrega $entregaId já está PROCESSADA; Silver contém $silver observação(ões). Nenhum reprocessamento foi feito."
            return
        }
        if($status -in @('REJEITADA','QUARENTENA')){throw "Entrega $entregaId está em estado terminal $status."}

        $otherPending=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM ingestao.lote WHERE entrega_id<>'$entregaId' AND status IN(N'PENDENTE',N'VALIDANDO',N'PROCESSANDO');")
        if($otherPending -ne 0){Write-Host "Existem $otherPending lote(s) pendentes de outras Entregas; serão preservados porque o Processor one-shot será filtrado pela Entrega atual."}
        $targetPending=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM ingestao.lote WHERE entrega_id='$entregaId' AND status IN(N'PENDENTE',N'VALIDANDO',N'PROCESSANDO');")
        if($targetPending -eq 0){throw "Entrega $entregaId não possui lote pendente para o Processor (status=$status)."}

        $resident=@()
        foreach($node in @('jornada-node1','jornada-node2')){
            $found=(& docker compose --env-file $EnvFile exec -T $node sh -lc "pgrep -af '[J]ornada.Processor.Worker.dll' || true" | Out-String).Trim()
            if(-not [string]::IsNullOrWhiteSpace($found)){$resident+=("${node}: $found")}
        }
        if($resident.Count -gt 0){throw "Processor residente detectado em modo manual: $($resident -join '; ')."}

        Write-Host "Executando Jornada.Processor.Worker one-shot para a Entrega ${entregaId}: no máximo um lote será processado neste clique."
        Invoke-Compose @('exec','-T','jornada-node2','env','Processor__Operation=PROCESS_ONE',"Processor__TargetEntregaId=$entregaId",'dotnet','/opt/jornada/apps/Jornada.Processor.Worker/Jornada.Processor.Worker.dll')

        $final=Invoke-SqlScalar "SELECT status FROM ingestao.entrega WHERE entrega_id='$entregaId';"
        $remainingTarget=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM ingestao.lote WHERE entrega_id='$entregaId' AND status IN(N'PENDENTE',N'VALIDANDO',N'PROCESSANDO');")
        $silverPeople=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM silver.pessoa_observacao po JOIN ingestao.lote l ON l.lote_id=po.lote_id WHERE l.entrega_id='$entregaId';")
        $silverFacts=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM silver.registro_observacao ro JOIN ingestao.lote l ON l.lote_id=ro.lote_id WHERE l.entrega_id='$entregaId';")
        Write-Host "Processor one-shot concluiu: entrega=$entregaId; status=$final; lotes_restantes=$remainingTarget; pessoas_silver=$silverPeople; registros_silver=$silverFacts."
        if($final -eq 'PROCESSADA'){
            Write-Host "JORNADA_ONE_SHOT_COMPLETE=1"
            return
        }
        if($remainingTarget -gt 0){
            Write-Host "JORNADA_ONE_SHOT_PENDING=$remainingTarget"
            Write-Host "A Entrega ainda possui $remainingTarget lote(s) pendente(s). Execute novamente 4.1 · Processar um lote para avançar mais uma iteração."
            return
        }
        throw "Processor one-shot terminou sem lotes pendentes, mas a Entrega não está PROCESSADA; estado final=$final."
    }

    'blocking' { Invoke-ClusterAction 'blocking' }

    'bootstrap-corpus' {
        $null=Ensure-BootstrapCorpus
    }

    'calibrate-initial' {
        Ensure-ClusterRunning
        $eligible="status=N'ATIVO' AND ISNULL(amostra_metodo,N'')<>N'SEED_DEV_FIXO_NAO_TREINADO'"
        $activeCount=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE $eligible;")
        if($activeCount -gt 1){throw "Estado inválido: encontrados $activeCount modelos calibrados ATIVOS."}
        $reused=($activeCount -eq 1)

        if(-not $reused){
            $goldCount=[int64](Ensure-BootstrapCorpus)
            Write-Host "Gerando o modelo BOOTSTRAP inicial a partir da referência IBGE + Gold sintética ($goldCount pessoas)..."
            Invoke-ClusterAction 'calibrate'
            $activeCount=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE $eligible;")
            if($activeCount -ne 1){throw "Calibração inicial deveria deixar exatamente 1 modelo BOOTSTRAP ATIVO; atual=$activeCount."}
        } else {
            Write-Host 'Modelo calibrado ATIVO já existe; preservando a versão corrente sem recalibrar.'
        }

        $goldCount=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(DISTINCT vc.pessoa_uuid) FROM silver.pessoa_observacao o JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=o.pessoa_observacao_id WHERE o.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%' AND vc.status=N'RESOLVIDO';")
        $bootstrapGoldCount=$goldCount
        $modelId=Invoke-SqlScalar "SELECT TOP(1) CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE $eligible ORDER BY versao DESC;"
        $version=[int](Invoke-SqlScalar "SELECT TOP(1) versao FROM identidade.modelo_linkage WHERE $eligible ORDER BY versao DESC;")
        $unresolvedBootstrap=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM silver.pessoa_observacao o WHERE o.codigo_pessoa_origem LIKE N'SCALE-PEND-%' AND NOT EXISTS(SELECT 1 FROM identidade.v_vinculo_corrente vc WHERE vc.pessoa_observacao_id=o.pessoa_observacao_id AND vc.status=N'RESOLVIDO');")
        $bootstrapReadiness=$null
        if($RuntimeMode -eq 'DEV'){
            if($unresolvedBootstrap -gt 0){
                Write-Host "DEV: avaliando o corpus adicional pela execução real do Linkage Runner; ainda inconclusivos/sem avaliação=$unresolvedBootstrap..."
                Invoke-ClusterAction 'linkage'
            }
            $bootstrapReadiness=Get-DevBootstrapLinkageReadiness -ModelId $modelId -ModelVersion $version
            if([int64]$bootstrapReadiness.unevaluatedByActiveModel -ne 0){
                throw "Infraestrutura DEV não pode ficar pronta com observações bootstrap sem avaliação pelo modelo ATIVO; atual=$($bootstrapReadiness.unevaluatedByActiveModel)."
            }
            if([int64]$bootstrapReadiness.falsePositiveAssociation -ne 0){
                throw "Infraestrutura DEV recusada: corpus bootstrap produziu $($bootstrapReadiness.falsePositiveAssociation) associação(ões) probabilística(s) incorreta(s) contra o ground truth sintético."
            }
            if([int64]$bootstrapReadiness.unexpectedNewIdentityAdditional -ne 0){
                throw "Infraestrutura DEV recusada: corpus bootstrap publicou $($bootstrapReadiness.unexpectedNewIdentityAdditional) NOVA_IDENTIDADE fora da coorte sintética deliberadamente sem candidato."
            }
            if([int64]$bootstrapReadiness.inconclusiveAdditional -gt 0){
                Write-Host "DEV: $($bootstrapReadiness.inconclusiveAdditional) observação(ões) SCALE-PEND permanecem inconclusivas por política estatística. Elas já foram avaliadas pelo modelo ATIVO e não constituem backlog de processamento."
            }
        }elseif($unresolvedBootstrap -ne 0){
            throw "Infraestrutura $RuntimeMode não pode ficar pronta com corpus adicional pendente; atual=$unresolvedBootstrap."
        }
        $remainingScale=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-%';")
        $bootstrapLifecycle=if($RuntimeMode -eq 'DEV'){'DEV_EVALUATED_PRESERVED'}else{'STANDARD_NO_EXTRA_PENDING'}
        if($RuntimeMode -eq 'DEV'){
            Write-Host "DEV preserva os 6.000 registros adicionais na Silver: resoluções seguras entram na Gold; resultados inconclusivos permanecem auditáveis sem forçar vínculo."
        }else{
            Write-Host "$RuntimeMode usa o comportamento padrão: nenhum corpus adicional de 6.000 registros é criado."
        }
        $bundleVersion=Invoke-SqlScalar "SELECT TOP(1) ISNULL(model_config_bundle_version,N'') FROM identidade.modelo_linkage WHERE $eligible ORDER BY versao DESC;"
        $fingerprint=Invoke-SqlScalar "SELECT TOP(1) ISNULL(model_config_bundle_fingerprint_sha256,N'') FROM identidade.modelo_linkage WHERE $eligible ORDER BY versao DESC;"
        $sampleMethod=Invoke-SqlScalar "SELECT TOP(1) ISNULL(amostra_metodo,N'') FROM identidade.modelo_linkage WHERE $eligible ORDER BY versao DESC;"
        $result=[ordered]@{
            generatedAt=(Get-Date).ToUniversalTime().ToString('o')
            mode=if($reused){'REUSED_ACTIVE'}else{'CREATED_AND_ACTIVATED_BOOTSTRAP'}
            modelRole=if($reused){'ACTIVE_CURRENT'}else{'BOOTSTRAP'}
            bootstrapReference='IBGE_CENSO_2022'
            bootstrapGoldPeople=$bootstrapGoldCount
            operationalScalePeopleAfterBootstrap=$remainingScale
            bootstrapCorpusLifecycle=$bootstrapLifecycle
            bootstrapLinkageReadiness=$bootstrapReadiness
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

        $lastIngestion=Join-Path $OutDir 'last-ingestion.json'
        if(-not(Test-Path $lastIngestion)){throw 'Nenhuma ingestão registrada pela Console DEV. Envie um ZIP antes de executar o linkage incremental.'}
        $saved=Get-Content $lastIngestion -Raw | ConvertFrom-Json
        $entregaId=[string]$saved.receipt.entregaId
        if([string]::IsNullOrWhiteSpace($entregaId)){$entregaId=[string]$saved.receipt.EntregaId}
        if([string]::IsNullOrWhiteSpace($entregaId)){throw 'Recibo da última ingestão não contém entregaId.'}
        $savedDb=[string]$saved.database
        $exists=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM ingestao.entrega WHERE entrega_id='$entregaId';")
        if(($savedDb -and $savedDb -ne $db) -or $exists -eq 0){
            throw "A última ingestão não pertence ao ambiente atual ($db). Envie um novo ZIP antes de executar o linkage incremental."
        }
        $deliveryStatus=Invoke-SqlScalar "SELECT status FROM ingestao.entrega WHERE entrega_id='$entregaId';"
        if($deliveryStatus -ne 'PROCESSADA'){
            throw "Linkage exige a Entrega PROCESSADA; atual=$deliveryStatus. Execute primeiro 'Processar Bronze → Silver'."
        }

        $idsRaw=Invoke-SqlScalar "SELECT STRING_AGG(CONVERT(varchar(max),po.pessoa_observacao_id),',') WITHIN GROUP (ORDER BY po.pessoa_observacao_id) FROM silver.pessoa_observacao po JOIN ingestao.lote l ON l.lote_id=po.lote_id LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id WHERE l.entrega_id='$entregaId' AND po.cpf IS NULL AND (vc.pessoa_observacao_id IS NULL OR vc.status IN(N'NAO_RESOLVIDO',N'CONFLITO') OR vc.metodo_resolucao=N'PENDENTE_PROBABILISTICO');"
        $observationIds=@()
        if(-not [string]::IsNullOrWhiteSpace($idsRaw)){
            $observationIds=@($idsRaw.Split(',') | ForEach-Object {[long]$_.Trim()})
        }
        Write-Host "Linkage incremental da última entrega: $entregaId"
        Write-Host "Observações sem CPF elegíveis nesta entrega: $($observationIds.Count)"
        if($observationIds.Count -eq 0){
            $cpfUnresolved=Invoke-SqlScalar "SELECT STRING_AGG(CONCAT(CONVERT(varchar(max),po.pessoa_observacao_id),N':',ISNULL(vc.status,N'SEM_VINCULO'),N':',ISNULL(vc.motivo,N'SEM_MOTIVO')),N', ') WITHIN GROUP (ORDER BY po.pessoa_observacao_id) FROM silver.pessoa_observacao po JOIN ingestao.lote l ON l.lote_id=po.lote_id LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id WHERE l.entrega_id='$entregaId' AND po.cpf IS NOT NULL AND (vc.pessoa_observacao_id IS NULL OR vc.status<>N'RESOLVIDO' OR vc.pessoa_uuid IS NULL);"
            if(-not [string]::IsNullOrWhiteSpace($cpfUnresolved)){
                throw "A Entrega não exige linkage probabilístico, mas há observação(ões) com CPF sem resolução determinística: $cpfUnresolved. Verifique o vínculo de identidade; não execute Linkage para corrigir CPF."
            }
            $cpfMissingGold=Invoke-SqlScalar "SELECT STRING_AGG(CONVERT(varchar(max),po.pessoa_observacao_id),N',') WITHIN GROUP (ORDER BY po.pessoa_observacao_id) FROM silver.pessoa_observacao po JOIN ingestao.lote l ON l.lote_id=po.lote_id JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id LEFT JOIN gold.pessoa g ON g.pessoa_uuid=vc.pessoa_uuid WHERE l.entrega_id='$entregaId' AND po.cpf IS NOT NULL AND vc.status=N'RESOLVIDO' AND vc.pessoa_uuid IS NOT NULL AND g.pessoa_uuid IS NULL;"
            if(-not [string]::IsNullOrWhiteSpace($cpfMissingGold)){
                throw "Inconsistência: observação(ões) com CPF foram resolvidas deterministicamente, mas não estão materializadas em gold.pessoa: $cpfMissingGold."
            }
            Write-Host 'Nenhuma observação desta entrega exige linkage probabilístico. As observações com CPF já estão resolvidas deterministicamente e publicadas na Gold.'
            return
        }

        $eligibleActive=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status=N'ATIVO' AND ISNULL(amostra_metodo,N'')<>N'SEED_DEV_FIXO_NAO_TREINADO';")
        if($eligibleActive -ne 1){
            throw "Executar linkage exige exatamente 1 modelo ATIVO; atual=$eligibleActive. A subida da infraestrutura deve garantir o BOOTSTRAP inicial (IBGE + corpus sintético). Execute novamente 'Preparar ambiente completo' para reparar/confirmar o estado. O seed fixo não libera linkage."
        }

        foreach($observationId in $observationIds){
            Write-Host "Processando pessoa_observacao_id=$observationId (escopo exclusivo da entrega $entregaId)..."
            Invoke-Compose @('exec','-T','jornada-node2','dotnet','/opt/jornada/apps/Jornada.Linkage.Runner/Jornada.Linkage.Runner.dll','--mode','ON_DEMAND','--pessoa-observacao-id',([string]$observationId),'--publish','true','--requested-by','DEV_CONSOLE','--reason',"dev-console-entrega:$entregaId")
        }
        Write-Host "Linkage incremental concluído para $($observationIds.Count) observação(ões) da entrega $entregaId. Backlog pendente de outras cargas não foi selecionado."
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
