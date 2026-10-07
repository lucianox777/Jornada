# DT-05: executado por local-e2e.ps1 -Dt05CpfLate, no mesmo escopo de API,
# Processor, conexão SQL, credencial DEV e helpers HTTP. Não invocar isoladamente.
$ErrorActionPreference = 'Stop'
if (-not $Dt05CpfLate -or -not $VerifyLinkageRunner -or $db -ne 'JornadaSyntheticDev') {
    throw 'DT-05: o ensaio exige local-e2e.ps1 -VerifyLinkageRunner -Dt05CpfLate e JornadaSyntheticDev.'
}
if ([int](Scalar "SELECT COUNT_BIG(*) FROM identidade.modelo_linkage WHERE status=N'ATIVO';") -ne 1) {
    throw 'DT-05: esperado exatamente um modelo ATIVO no corpus DEV.'
}
# A tabela controle.modo_carga_inicial foi removida pela migration de 27/09.
# O E2E usa JornadaSyntheticDev isolado, exige exatamente um modelo ATIVO
# e o Runner aplica a coordenação atual do pipeline antes de publicar.

# Duas referências DEV deliberadamente indistinguíveis forçam CONFLITO na
# observação sem CPF. A referência principal já existe no Jornada_Seed_Dev.
# A duplicata não recebe CPF ou âncora; nenhum dado de cidadão entra no ensaio.
$referenceCpf = '11144477735' # exclusivamente CPF sintético do seed do repositório
$referenceUuid = (Scalar "SELECT CONVERT(VARCHAR(36),pessoa_uuid) FROM identidade.cpf_ancora WHERE cpf='$referenceCpf';")
if (-not $referenceUuid) { throw 'DT-05: âncora sintética do seed não encontrada.' }
Sql @'
DECLARE @clone UNIQUEIDENTIFIER=NEWID();
INSERT identidade.pessoa(pessoa_uuid,status) VALUES(@clone,N'ATIVO');
INSERT gold.pessoa(
 pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,
 fontes_distintas,estado_concordancia,atualizado_em,estado_identidade)
VALUES(
 @clone,NULL,N'SEM_CPF',N'Maria da Silva','1982-04-10',N'Ana de Souza',
 1,N'BASELINE_FONTE_UNICA',SYSUTCDATETIME(),N'REFERENCIA');
'@ | Out-Null
if ([int](Scalar "SELECT COUNT_BIG(*) FROM gold.pessoa WHERE estado_identidade=N'REFERENCIA' AND nome_completo=N'Maria da Silva' AND data_nascimento='1982-04-10' AND nome_mae=N'Ana de Souza';") -ne 2) {
    throw 'DT-05: a colisão sintética controlada exige exatamente duas referências equivalentes.'
}

if ($Dt05HistoricalReplay) {
    Sql "INSERT identidade.blocking_chave(pessoa_uuid,normalizacao_versao,atributo,valor_normalizado,semantica_temporal) SELECT g.pessoa_uuid,N'IDENTITY_NORMALIZATION_V1',N'birth_year',N'1982',N'STABLE_IDENTITY_DATUM' FROM gold.pessoa g WHERE g.estado_identidade=N'REFERENCIA' AND g.nome_completo=N'Maria da Silva' AND g.data_nascimento='1982-04-10' AND g.nome_mae=N'Ana de Souza' AND NOT EXISTS(SELECT 1 FROM identidade.blocking_chave k WHERE k.pessoa_uuid=g.pessoa_uuid AND k.normalizacao_versao=N'IDENTITY_NORMALIZATION_V1' AND k.atributo=N'birth_year' AND k.valor_normalizado=N'1982' AND k.vigencia_fim IS NULL);" | Out-Null
    if ([int](Scalar "SELECT COUNT_BIG(*) FROM identidade.blocking_chave WHERE atributo=N'birth_year' AND valor_normalizado=N'1982' AND vigencia_fim IS NULL;") -lt 2) {
        throw 'DT-05: fixture não materializou as duas chaves birth_year sintéticas.'
    }
    $modelId = Scalar "SELECT CONVERT(VARCHAR(36),modelo_id) FROM identidade.modelo_linkage WHERE status=N'ATIVO';"
    $algorithm = Scalar "SELECT algoritmo_versao FROM identidade.modelo_linkage WHERE modelo_id='$modelId';"
    $ruleVersion = 'DT05_E2E_DYNAMIC_BLOCKING_V1'
    $parameterRows = @(Sql "SELECT CONCAT(nome,N'|',CONVERT(VARCHAR(100),valor)) FROM identidade.parametro_linkage WHERE modelo_id='$modelId' ORDER BY nome;")
    [Reflection.Assembly]::LoadFrom((Join-Path $Root 'src/Jornada.Contracts/bin/Release/net10.0/Jornada.Contracts.dll')) | Out-Null
    $pass = [Jornada.Contracts.LinkageBlockingPass]::Create('P001',[string[]]@('birth_year'))
    $parameters = [System.Collections.Generic.List[System.Collections.Generic.KeyValuePair[string,decimal]]]::new()
    foreach ($row in $parameterRows) {
        $parts = $row.Split('|',2)
        $value = [decimal]::Parse($parts[1],[Globalization.CultureInfo]::InvariantCulture)
        $parameters.Add([System.Collections.Generic.KeyValuePair[string,decimal]]::new($parts[0],$value))
    }
    $rule = [Jornada.Contracts.LinkageDynamicRuleSet]::CreateWithPasses($ruleVersion,$algorithm,[Jornada.Contracts.LinkageBlockingPass[]]@($pass),$parameters)
    $rulesetFingerprint = $rule.FingerprintSha256
    Sql @"
DISABLE TRIGGER identidade.tr_linkage_ruleset_insert_status ON identidade.linkage_ruleset;
DISABLE TRIGGER identidade.tr_linkage_ruleset_passe_guard ON identidade.linkage_ruleset_passe;
DISABLE TRIGGER identidade.tr_linkage_ruleset_campo_guard ON identidade.linkage_ruleset_passe_campo;
BEGIN TRY
 INSERT identidade.linkage_ruleset(ruleset_id,modelo_id,ruleset_versao,algoritmo_versao,fingerprint_sha256,projection_schema_version,projection_fingerprint_sha256)
 VALUES('$modelId','$modelId',N'$ruleVersion',N'$algorithm','$rulesetFingerprint',N'PERSON_RESOLUTION_PROJECTION_V2','d186f28c51e18802f7c2df5b8b192b6278d28874833c8d824d483608aa3abbb4');
 INSERT identidade.linkage_ruleset_passe(ruleset_id,passe_ordem,passe_id) VALUES('$modelId',0,N'P001');
 INSERT identidade.linkage_ruleset_passe_campo(ruleset_id,passe_ordem,campo_ordem,atributo) VALUES('$modelId',0,0,N'birth_year');
END TRY
BEGIN CATCH
 ENABLE TRIGGER identidade.tr_linkage_ruleset_insert_status ON identidade.linkage_ruleset;
 ENABLE TRIGGER identidade.tr_linkage_ruleset_passe_guard ON identidade.linkage_ruleset_passe;
 ENABLE TRIGGER identidade.tr_linkage_ruleset_campo_guard ON identidade.linkage_ruleset_passe_campo;
 THROW;
END CATCH;
ENABLE TRIGGER identidade.tr_linkage_ruleset_insert_status ON identidade.linkage_ruleset;
ENABLE TRIGGER identidade.tr_linkage_ruleset_passe_guard ON identidade.linkage_ruleset_passe;
ENABLE TRIGGER identidade.tr_linkage_ruleset_campo_guard ON identidade.linkage_ruleset_passe_campo;
"@ | Out-Null
    if ([int](Scalar "SELECT COUNT_BIG(*) FROM identidade.linkage_ruleset WHERE modelo_id='$modelId' AND fingerprint_sha256='$rulesetFingerprint';") -ne 1) {
        throw 'DT-05: fixture não conseguiu anexar ruleset dinâmico íntegro ao modelo DEV isolado.'
    }
}

$marker = [Guid]::NewGuid().ToString('N')
$originCode = "DT05-CPF-LATE-$marker"
$fixtureRoot = Join-Path $Out 'dt05-fixtures'
New-Item -ItemType Directory -Force $fixtureRoot | Out-Null

function New-Dt05DeliveryPackage([int]$wave, [string]$cpf) {
    $dir = Join-Path $fixtureRoot "wave$wave"
    New-Item -ItemType Directory -Force $dir | Out-Null
    $manifest = [ordered]@{
        formatoVersao = 2; pessoaSchemaVersao = 6
        codigoSistemaOrigem = 'SEHAB'; natureza = 'BENEFICIO'
        codigoTipo = 'AA01'; tipoVersao = 1
        dataReferencia = if ($wave -eq 1) { '2026-08-27T00:00:00-03:00' } else { '2026-08-28T00:00:00-03:00' }
    }
    $person = [ordered]@{
        idPessoaEntrega = 'DT05-PESSOA-001'
        codigoPessoaOrigem = $originCode
        cpf = if ($cpf) { $cpf } else { $null }
        cpfAusenteMotivo = if ($cpf) { $null } else { 'SEM_CPF' }
        nomeCompleto = 'Maria da Silva'
        dataNascimento = '1982-04-10'
        nomeMae = 'Ana de Souza'
        sourceTransactionId = "DT05-SYNTH-$marker-W$wave"
    }
    Write-Utf8NoBom (Join-Path $dir 'manifest.json') (($manifest | ConvertTo-Json -Compress -Depth 5) + [Environment]::NewLine)
    Write-Utf8NoBom (Join-Path $dir 'pessoas.jsonl') (($person | ConvertTo-Json -Compress -Depth 5) + [Environment]::NewLine)
    Write-Utf8NoBom (Join-Path $dir 'registros.jsonl') ''
    $built = (& python (Join-Path $Root 'scripts/build-ingestion-fixture.py') --fixture $dir --gestor SEHAB --output-dir (Join-Path $Out 'packages') | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $built -PathType Leaf)) {
        throw "DT-05: falha ao gerar fixture sintética da onda $wave."
    }
    return $built
}

function Send-Dt05Delivery([int]$wave, [string]$cpf) {
    $script:package = New-Dt05DeliveryPackage $wave $cpf
    $script:filename = Split-Path -Leaf $script:package
    $post = Join-Path $Out "dt05-post-wave$wave.json"
    $codeFile = Join-Path $Out "dt05-post-wave$wave.code"
    Post-Delivery "dt05-$marker-wave$wave" $post $codeFile
    $statusCode = (Get-Content -Raw $codeFile).Trim()
    if ($statusCode -ne '202') {
        throw "DT-05: POST da onda $wave retornou HTTP $statusCode. $(Get-Content -Raw $post)"
    }
    $deliveryId = (Read-Json $post).entregaId
    if (-not $deliveryId) { throw "DT-05: entrega da onda $wave sem ID." }
    Wait-Processed $deliveryId (Join-Path $Out "dt05-status-wave$wave.json")
    return $deliveryId
}

function Invoke-Dt05Runner([int]$wave, [long]$observationId) {
    $reason = "$marker-W$wave"
    Push-Location $Root
    try {
        dotnet run --no-build --configuration Release --project src/Jornada.Linkage.Runner -- --mode ON_DEMAND --pessoa-observacao-id $observationId --max-records 1 --requested-by DT05_CPF_LATE_E2E --reason $reason --publish true | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "DT-05: Runner real falhou na onda $wave." }
    } finally { Pop-Location }
    $runId = Scalar "SELECT CONVERT(VARCHAR(36),linkage_run_id) FROM identidade.linkage_run WHERE solicitado_por=N'DT05_CPF_LATE_E2E' AND motivo=N'$reason' AND status=N'PUBLICADO';"
    if (-not $runId -or [int](Scalar "SELECT COUNT_BIG(*) FROM identidade.linkage_run WHERE solicitado_por=N'DT05_CPF_LATE_E2E' AND motivo=N'$reason' AND status=N'PUBLICADO';") -ne 1) {
        throw "DT-05: Runner da onda $wave não publicou exatamente um run."
    }
    $resultId = Scalar "SELECT CONVERT(VARCHAR(30),linkage_resultado_id) FROM identidade.linkage_resultado WHERE linkage_run_id='$runId' AND pessoa_observacao_id=$observationId;"
    if (-not $resultId -or [int](Scalar "SELECT COUNT_BIG(*) FROM identidade.linkage_resultado WHERE linkage_run_id='$runId';") -ne 1) {
        throw "DT-05: onda $wave não preservou exatamente um resultado bruto."
    }
    return [pscustomobject]@{ wave = $wave; runId = $runId; resultId = [long]$resultId; reason = $reason }
}

# Onda 1: Bronze -> Processor -> Silver sem CPF -> Runner real.
$delivery1 = Send-Dt05Delivery 1 ''
$observationId = [long](Scalar "SELECT po.pessoa_observacao_id FROM silver.pessoa_observacao po JOIN ingestao.lote l ON l.lote_id=po.lote_id WHERE l.entrega_id='$delivery1' AND po.codigo_pessoa_origem='$originCode' AND po.cpf IS NULL;")
$sourceId = [long](Scalar "SELECT pessoa_origem_id FROM silver.pessoa_observacao WHERE pessoa_observacao_id=$observationId;")
$version1 = [int](Scalar "SELECT versao_interna FROM silver.pessoa_observacao WHERE pessoa_observacao_id=$observationId;")
if ($observationId -le 0 -or $sourceId -le 0) { throw 'DT-05: onda 1 não gerou Silver sem CPF ancorada em origem persistente.' }
$wave1 = Invoke-Dt05Runner 1 $observationId
$raw1 = Scalar "SELECT CONCAT(status,N'|',resultado_publicacao,N'|',status_publicacao) FROM identidade.linkage_resultado WHERE linkage_resultado_id=$($wave1.resultId);"
if ($raw1 -ne 'CONFLITO|INDEFINIDA|CONFLITO') {
    throw "DT-05: precondição de conflito sintético não foi satisfeita. Obtido: $raw1"
}
if ((Scalar "SELECT estado FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=$sourceId;") -ne 'INDEFINIDA') {
    throw 'DT-05: origem deveria continuar INDEFINIDA antes da chegada determinística do CPF.'
}
if ([int](Scalar "SELECT COUNT_BIG(*) FROM identidade.linkage_transicao_semantica WHERE pessoa_observacao_id=$observationId AND transicao_tipo=N'INICIAL' AND assinatura_anterior_sha256 IS NULL;") -ne 1) {
    throw 'DT-05: onda 1 não emitiu INICIAL com hash anterior nulo.'
}

# Onda 2: o MESMO ID Silver sem CPF é reavaliado sem qualquer mutação de corpus.
$wave2 = Invoke-Dt05Runner 2 $observationId
$raw2 = Scalar "SELECT CONCAT(status,N'|',resultado_publicacao,N'|',status_publicacao) FROM identidade.linkage_resultado WHERE linkage_resultado_id=$($wave2.resultId);"
if ($raw2 -ne $raw1) { throw "DT-05: ondas 1 e 2 divergiram antes da chegada do CPF: $raw1 / $raw2." }
# Exigir igualdade dos DOZE campos da assinatura V1, não só dos três estados.
$signatureFields = 'modelo_id,modelo_versao,status,motivo,resultado_publicacao,pessoa_uuid_publicado,status_publicacao,motivo_publicacao,melhor_candidato_uuid,segundo_candidato_uuid,politica_publicacao_versao,pessoa_origem_id_publicado'
$replaySemanticSignatureFields = 'modelo_id,modelo_versao,status,motivo,melhor_candidato_uuid,segundo_candidato_uuid'
$semanticDifference = [int](Scalar "SELECT COUNT_BIG(*) FROM (SELECT $signatureFields FROM identidade.linkage_resultado WHERE linkage_resultado_id=$($wave1.resultId) EXCEPT SELECT $signatureFields FROM identidade.linkage_resultado WHERE linkage_resultado_id=$($wave2.resultId)) AS diferencas;")
if ($semanticDifference -ne 0) { throw 'DT-05: os doze campos da assinatura V1 mudaram indevidamente na onda 2.' }
if ([int](Scalar "SELECT COUNT_BIG(*) FROM identidade.linkage_transicao_semantica WHERE pessoa_observacao_id=$observationId;") -ne 1) {
    throw 'DT-05: a onda 2 gerou transição apesar da assinatura idêntica.'
}

# Onda 3: NOVA entrega real, MESMO codigoPessoaOrigem, nova versão Silver COM CPF.
$delivery3 = Send-Dt05Delivery 3 $referenceCpf
$lateObservationId = [long](Scalar "SELECT po.pessoa_observacao_id FROM silver.pessoa_observacao po JOIN ingestao.lote l ON l.lote_id=po.lote_id WHERE l.entrega_id='$delivery3' AND po.pessoa_origem_id=$sourceId AND po.cpf='$referenceCpf' AND po.versao_interna>$version1;")
if ($lateObservationId -le 0 -or $lateObservationId -eq $observationId) {
    throw 'DT-05: nova versão append-only Silver com CPF tardio real não foi materializada.'
}
$lateEvidence = [int](Scalar "SELECT COUNT_BIG(*) FROM identidade.vinculo_fonte v JOIN identidade.cpf_ancora a ON a.pessoa_uuid=v.pessoa_uuid WHERE v.pessoa_observacao_id=$lateObservationId AND v.metodo_resolucao=N'CPF_DETERMINISTICO' AND v.status=N'RESOLVIDO' AND a.cpf='$referenceCpf';")
if ($lateEvidence -ne 1) { throw 'DT-05: Processor não comprovou vínculo determinístico do novo CPF na nova Silver.' }
$referenceConfirmed = [int](Scalar "SELECT COUNT_BIG(*) FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=$sourceId AND estado=N'REFERENCIA' AND canonical_uuid='$referenceUuid';")
if ($referenceConfirmed -ne 1) { throw 'DT-05: CPF tardio não atualizou a referência progressiva da MESMA origem.' }
if ((Scalar "SELECT COUNT_BIG(*) FROM silver.pessoa_observacao WHERE pessoa_observacao_id=$observationId AND cpf IS NULL;") -ne '1') {
    throw 'DT-05: a observação original foi adulterada; Silver deveria ser append-only.'
}

# O Runner probabilístico continua restrito à observação antiga, ainda sem CPF.
# A nova versão com CPF modifica a referência progressiva da origem e, com isso,
# os campos V1 publicados na terceira reavaliação: não há marcador de teste.
$wave3 = Invoke-Dt05Runner 3 $observationId
$allRaw = [int](Scalar "SELECT COUNT_BIG(*) FROM identidade.linkage_resultado WHERE pessoa_observacao_id=$observationId AND linkage_run_id IN('$($wave1.runId)','$($wave2.runId)','$($wave3.runId)');")
$distinctRuns = [int](Scalar "SELECT COUNT(DISTINCT linkage_run_id) FROM identidade.linkage_resultado WHERE pessoa_observacao_id=$observationId AND linkage_run_id IN('$($wave1.runId)','$($wave2.runId)','$($wave3.runId)');")
if ($allRaw -ne 3 -or $distinctRuns -ne 3) { throw 'DT-05: os três resultados brutos independentes não foram preservados.' }
$transitions = @(Sql "SELECT CONCAT(transicao_tipo,N'|',CONVERT(VARCHAR(64),assinatura_sha256,2),N'|',COALESCE(CONVERT(VARCHAR(64),assinatura_anterior_sha256,2),N'NULL'),N'|',CONVERT(VARCHAR(36),linkage_run_id)) FROM identidade.linkage_transicao_semantica WHERE pessoa_observacao_id=$observationId ORDER BY transicao_id;")
if ($transitions.Count -ne 2) { throw "DT-05: esperado EXATAMENTE INICIAL e ALTERACAO_SEMANTICA, encontrado $($transitions.Count)." }
$first = $transitions[0].Split('|')
$second = $transitions[1].Split('|')
if ($first.Count -ne 4 -or $second.Count -ne 4 -or
    $first[0] -ne 'INICIAL' -or $second[0] -ne 'ALTERACAO_SEMANTICA' -or
    $first[2] -ne 'NULL' -or $first[1] -eq $second[1] -or
    $second[2] -ne $first[1] -or
    $first[3] -ne $wave1.runId -or $second[3] -ne $wave3.runId) {
    throw 'DT-05: sequência/encadeamento SHA-256 incorreto ou onda 2 gerou uma transição.'
}
$reason3 = Scalar "SELECT motivo_publicacao FROM identidade.linkage_resultado WHERE linkage_resultado_id=$($wave3.resultId);"
if ($reason3 -notlike 'REFERENCIA_PROGRESSIVA_*') {
    throw "DT-05: terceira onda não refletiu a chegada real da referência progressiva: $reason3."
}
$cpfFingerprint = Scalar "SELECT CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONVERT(VARBINARY(MAX),cpf)),2) FROM silver.pessoa_observacao WHERE pessoa_observacao_id=$lateObservationId;"
$evidence = [ordered]@{
    gate = 'DT05_CPF_LATE_REAL_RUNNER_E2E'
    status = 'PASS'
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    githubRunId = $env:GITHUB_RUN_ID
    gitSha = $env:GITHUB_SHA
    database = $db
    corpus = 'Jornada_Seed_Dev / colisão sintética controlada'
    observationId = $observationId
    pessoaOrigemId = $sourceId
    lateCpf = [ordered]@{
        novaObservacaoId = $lateObservationId
        primeiraVersao = $version1
        ultimaVersao = [int](Scalar "SELECT versao_interna FROM silver.pessoa_observacao WHERE pessoa_observacao_id=$lateObservationId;")
        fingerprintSha256 = $cpfFingerprint
        processadoViaHttpBronzeProcessor = $true
        vinculoCpfDeterministico = $true
        referenciaProgressivaConfirmada = $true
    }
    runs = @($wave1,$wave2,$wave3)
    rawResultsPreserved = $allRaw
    semanticTransitions = 2
    initialSha256 = $first[1]
    changedSha256 = $second[1]
    previousSha256 = $second[2]
    wave2NoTransition = $true
}
$path = Join-Path $Out 'dt05-cpf-late-evidence.json'
$evidence | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $path
Get-Content -Raw $path

if ($Dt05HistoricalReplay) {
    $replayReason = "$marker-HISTORICAL-REPLAY"
    Push-Location $Root
    try {
        dotnet run --no-build --configuration Release --project src/Jornada.Linkage.Runner -- --mode REPLAY --replay-source-run-id $($wave1.runId) --requested-by DT05_HISTORICAL_REPLAY_E2E --reason $replayReason --publish false | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'DT-05: REPLAY histórico real falhou.' }
    } finally { Pop-Location }
    $replayRunId = Scalar "SELECT CONVERT(VARCHAR(36),linkage_run_id) FROM identidade.linkage_run WHERE solicitado_por=N'DT05_HISTORICAL_REPLAY_E2E' AND motivo=N'$replayReason';"
    if (-not $replayRunId) { throw 'DT-05: run de replay histórico não foi persistido.' }
    $sourceCount = [int](Scalar "SELECT COUNT_BIG(*) FROM identidade.linkage_run_item WHERE linkage_run_id='$($wave1.runId)';")
    $replayCount = [int](Scalar "SELECT COUNT_BIG(*) FROM identidade.linkage_run_item WHERE linkage_run_id='$replayRunId';")
    $universeDelta = [int](Scalar "SELECT COUNT_BIG(*) FROM (SELECT pessoa_observacao_id FROM identidade.linkage_run_item WHERE linkage_run_id='$($wave1.runId)' EXCEPT SELECT pessoa_observacao_id FROM identidade.linkage_run_item WHERE linkage_run_id='$replayRunId') d;")
    if ($sourceCount -ne $replayCount -or $universeDelta -ne 0) { throw 'DT-05: REPLAY não reproduziu exatamente o universo do source run.' }
    $replayResultId = [long](Scalar "SELECT linkage_resultado_id FROM identidade.linkage_resultado WHERE linkage_run_id='$replayRunId' AND pessoa_observacao_id=$observationId;")
    if ($replayResultId -le 0) { throw 'DT-05: REPLAY não produziu resultado bruto para a observação histórica.' }
    $resultDelta = [int](Scalar "SELECT COUNT_BIG(*) FROM (SELECT $replaySemanticSignatureFields FROM identidade.linkage_resultado WHERE linkage_resultado_id=$($wave1.resultId) EXCEPT SELECT $replaySemanticSignatureFields FROM identidade.linkage_resultado WHERE linkage_resultado_id=$replayResultId) d;")
    if ($resultDelta -ne 0) {
        $signatureNames = $signatureFields -split ','
        $diffParts = @()
        foreach ($name in ($replaySemanticSignatureFields -split ',')) {
            $sourceValue = Scalar "SELECT CONVERT(NVARCHAR(4000),[$name]) FROM identidade.linkage_resultado WHERE linkage_resultado_id=$($wave1.resultId);"
            $replayValue = Scalar "SELECT CONVERT(NVARCHAR(4000),[$name]) FROM identidade.linkage_resultado WHERE linkage_resultado_id=$replayResultId;"
            if ([string]$sourceValue -ne [string]$replayValue) { $diffParts += "$name=source[$sourceValue]/replay[$replayValue]" }
        }
        throw "DT-05: resultado do REPLAY divergiu da assinatura V1 do source run: $($diffParts -join '; ')."
    }
    $replayPublicationCount = [int](Scalar "SELECT COUNT_BIG(*) FROM identidade.linkage_resultado WHERE linkage_run_id='$replayRunId' AND (resultado_publicacao IS NOT NULL OR pessoa_uuid_publicado IS NOT NULL OR status_publicacao IS NOT NULL OR motivo_publicacao IS NOT NULL OR politica_publicacao_versao IS NOT NULL OR pessoa_origem_id_publicado IS NOT NULL);")
    if ($replayPublicationCount -ne 0) { throw 'DT-05: REPLAY executado com publish=false produziu efeitos de publicação.' }
    $binding = [int](Scalar "SELECT COUNT_BIG(*) FROM identidade.linkage_replay_manifesto WHERE linkage_run_id='$($wave1.runId)' AND schema_version=4 AND candidate_state_manifesto_sha256 IS NOT NULL AND blocking_projection_manifesto_sha256 IS NOT NULL;")
    if ($binding -ne 1) { throw 'DT-05: source run não possui binding v4 completo.' }
    $replayEvidence = [ordered]@{
        gate='DT05_HISTORICAL_REPLAY_DETERMINISM_E2E'; status='PASS'; generatedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
        githubRunId=$env:GITHUB_RUN_ID; gitSha=$env:GITHUB_SHA; database=$db
        sourceRunId=$wave1.runId; replayRunId=$replayRunId; sourceUniverse=$sourceCount; replayUniverse=$replayCount
        exactUniverse=$true; exactSemanticResult=$true; sourceManifestSchema=4
        currentCorpusWasMutatedAfterSource=$true; replayPublishedOperationalEffects=$false
    }
    $replayPath=Join-Path $Out 'dt05-historical-replay-evidence.json'
    $replayEvidence | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 $replayPath
    Get-Content -Raw $replayPath
    Write-Host 'DT-05 REPLAY HISTORICO DETERMINISTICO: PASS'
}
Write-Host 'DT-05 CPF TARDIO / RUNNER REAL: PASS'