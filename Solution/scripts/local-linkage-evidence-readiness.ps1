param()

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
$OutDir = Join-Path $Root '.local\linkage-evidence-readiness'
$ReportPath = Join-Path $OutDir 'evidence-readiness.json'

if (-not (Test-Path -LiteralPath $EnvFile)) { throw "Arquivo .env ausente em $Root." }
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker não encontrado no PATH.' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Format-CommandArgument {
    param([Parameter(Mandatory=$true)][AllowEmptyString()][string]$Value)
    if ($Value -notmatch '[\s''"$&|<>]') { return $Value }
    return "'" + $Value.Replace("'", "''") + "'"
}
function Write-CommandLine {
    param([Parameter(Mandatory=$true)][string]$Executable,[string[]]$Arguments=@())
    $tokens=@((Format-CommandArgument $Executable))
    $tokens+=@($Arguments | ForEach-Object { Format-CommandArgument ([string]$_) })
    Write-Host ("# " + ($tokens -join ' ')) -ForegroundColor DarkGray
}
function Get-EnvValue([string]$Name) {
    foreach ($line in Get-Content -LiteralPath $EnvFile) {
        if ($line -match '^\s*#' -or [string]::IsNullOrWhiteSpace($line)) { continue }
        $parts=$line -split '=',2
        if ($parts.Count -eq 2 -and $parts[0].Trim() -eq $Name) { return $parts[1].Trim() }
    }
    return $null
}

$password=Get-EnvValue 'JORNADA_SQL_SA_PASSWORD'
if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD ausente do .env.' }
$db=Get-EnvValue 'JORNADA_SQL_DATABASE'
if ([string]::IsNullOrWhiteSpace($db)) { $db='JornadaLocal' }

function Get-SqlLines([string]$Query) {
    Push-Location $Root
    try {
        $safeArgs=@('compose','--env-file',$EnvFile,'exec','-T','-e','SQLCMDPASSWORD=<redacted>','sqlserver','/opt/mssql-tools18/bin/sqlcmd','-S','localhost','-U','sa','-C','-b','-d',$db,'-W','-h','-1','-s','|','-Q',"SET NOCOUNT ON; $Query")
        Write-CommandLine 'docker' $safeArgs
        $dockerArgs=@('compose','--env-file',$EnvFile,'exec','-T','-e','SQLCMDPASSWORD','sqlserver','/opt/mssql-tools18/bin/sqlcmd','-S','localhost','-U','sa','-C','-b','-d',$db,'-W','-h','-1','-s','|','-Q',"SET NOCOUNT ON; $Query")
        $previousPassword=$env:SQLCMDPASSWORD
        try {
            $env:SQLCMDPASSWORD=$password
            $lines=@(& docker @dockerArgs)
        }
        finally {
            if ($null -eq $previousPassword) { Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue }
            else { $env:SQLCMDPASSWORD=$previousPassword }
        }
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd -Q falhou ($LASTEXITCODE)." }
        return @($lines | ForEach-Object { $_.Trim() } | Where-Object { $_ -and $_ -notmatch '^\([0-9]+ rows? affected\)$' })
    }
    finally { Pop-Location }
}
function Get-SqlScalar([string]$Query) {
    $lines=@(Get-SqlLines $Query)
    if ($lines.Count -eq 0) { return '' }
    return [string]$lines[-1]
}

$activeModelId=Get-SqlScalar "SELECT TOP(1) CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'')<>'SEED_DEV_FIXO_NAO_TREINADO' ORDER BY versao DESC;"
if ([string]::IsNullOrWhiteSpace($activeModelId)) { throw 'Nenhum modelo calibrado ATIVO.' }
$modelVersion=[int](Get-SqlScalar "SELECT versao FROM identidade.modelo_linkage WHERE modelo_id='$activeModelId';")
$algorithmVersion=Get-SqlScalar "SELECT algoritmo_versao FROM identidade.modelo_linkage WHERE modelo_id='$activeModelId';"
$modelShort=$activeModelId.Substring(0,8).ToLowerInvariant()

$runId=Get-SqlScalar @"
SELECT TOP(1) CONVERT(varchar(36),lr.linkage_run_id)
FROM identidade.linkage_run lr
CROSS APPLY (
    SELECT
      SUM(CASE WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-%' THEN 1 ELSE 0 END) AS pos_count,
      SUM(CASE WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-%' THEN 1 ELSE 0 END) AS neg_count,
      SUM(CASE WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-CONFLICT-%' THEN 1 ELSE 0 END) AS conflict_count
    FROM identidade.linkage_resultado r
    JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
    WHERE r.linkage_run_id=lr.linkage_run_id
) c
WHERE lr.status='PUBLICADO' AND lr.tipo_run='ON_DEMAND' AND lr.modelo_id='$activeModelId'
  AND c.pos_count=40 AND c.neg_count=40 AND c.conflict_count=10
ORDER BY lr.publicado_em DESC,lr.iniciado_em DESC,lr.linkage_run_id DESC;
"@
if ([string]::IsNullOrWhiteSpace($runId)) { throw "Execute primeiro '.\scripts\local-linkage-validation.ps1'." }

$projectionLine=Get-SqlScalar @"
SELECT CONCAT(rs.projection_schema_version,'|',rs.projection_fingerprint_sha256,'|',m.normalizacao_versao)
FROM identidade.linkage_ruleset rs
JOIN identidade.modelo_linkage m ON m.modelo_id=rs.modelo_id
WHERE rs.modelo_id='$activeModelId';
"@
$projection=$projectionLine.Split('|')
if ($projection.Count -ne 3) { throw "Proveniência da projeção inválida: $projectionLine" }
$projectionSchema=$projection[0]
$projectionFingerprint=$projection[1]
$normalizationVersion=$projection[2]

$coverageLines=@(Get-SqlLines @"
WITH base_rows AS (
    SELECT r.pessoa_observacao_id AS source_obs_id,r.melhor_candidato_uuid AS candidate_uuid,po.cpf AS source_cpf,
      CASE
        WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-EXACT-%' THEN N'POS_EXACT'
        WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-%' THEN N'POS_OTHER'
        WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-HARD_HOMONYM-%' THEN N'NEG_HARD_HOMONYM'
        WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-%' THEN N'NEG_OTHER'
        ELSE N'OTHER' END AS scenario_group
    FROM identidade.linkage_resultado r
    JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
    WHERE r.linkage_run_id='$runId'
      AND (po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-%'
        OR po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-%')
), segmented AS (
    SELECT source_obs_id,candidate_uuid,source_cpf,N'ALL' segment FROM base_rows
    UNION ALL SELECT source_obs_id,candidate_uuid,source_cpf,N'POS_ALL' FROM base_rows WHERE scenario_group LIKE N'POS_%'
    UNION ALL SELECT source_obs_id,candidate_uuid,source_cpf,N'NEG_ALL' FROM base_rows WHERE scenario_group LIKE N'NEG_%'
    UNION ALL SELECT source_obs_id,candidate_uuid,source_cpf,N'POS_EXACT' FROM base_rows WHERE scenario_group=N'POS_EXACT'
    UNION ALL SELECT source_obs_id,candidate_uuid,source_cpf,N'NEG_HARD_HOMONYM' FROM base_rows WHERE scenario_group=N'NEG_HARD_HOMONYM'
), flags AS (
    SELECT s.*,
      CASE WHEN s.source_cpf IS NOT NULL THEN 1 ELSE 0 END src_cpf,
      CASE WHEN EXISTS(SELECT 1 FROM silver.pessoa_identificador_observacao i WHERE i.pessoa_observacao_id=s.source_obs_id AND i.tipo_identificador_codigo='CNS' AND i.status_validacao<>'INVALIDO') THEN 1 ELSE 0 END src_cns,
      CASE WHEN EXISTS(SELECT 1 FROM silver.pessoa_identificador_observacao i WHERE i.pessoa_observacao_id=s.source_obs_id AND i.tipo_identificador_codigo='RG' AND i.status_validacao<>'INVALIDO') THEN 1 ELSE 0 END src_rg,
      CASE WHEN EXISTS(SELECT 1 FROM silver.pessoa_identificador_observacao i WHERE i.pessoa_observacao_id=s.source_obs_id AND i.tipo_identificador_codigo='UUID_JORNADA' AND i.status_validacao<>'INVALIDO') THEN 1 ELSE 0 END src_uuid,
      CASE WHEN EXISTS(SELECT 1 FROM silver.pessoa_atributo_observacao a WHERE a.pessoa_observacao_id=s.source_obs_id AND a.atributo_codigo='TELEFONE_CONTATO') THEN 1 ELSE 0 END src_phone,
      CASE WHEN EXISTS(SELECT 1 FROM silver.pessoa_atributo_observacao a WHERE a.pessoa_observacao_id=s.source_obs_id AND a.atributo_codigo='EMAIL_CONTATO') THEN 1 ELSE 0 END src_email,
      CASE WHEN EXISTS(SELECT 1 FROM silver.pessoa_atributo_observacao a WHERE a.pessoa_observacao_id=s.source_obs_id AND a.atributo_codigo='NOME_SOCIAL') THEN 1 ELSE 0 END src_social,
      CASE WHEN EXISTS(SELECT 1 FROM silver.pessoa_atributo_observacao a WHERE a.pessoa_observacao_id=s.source_obs_id AND a.atributo_codigo='ENDERECO_RESIDENCIAL') THEN 1 ELSE 0 END src_address,
      CASE WHEN EXISTS(SELECT 1 FROM silver.pessoa_atributo_observacao a WHERE a.pessoa_observacao_id=s.source_obs_id AND a.atributo_codigo='REFERENCIA_TERRITORIAL') THEN 1 ELSE 0 END src_territory,
      CASE WHEN EXISTS(SELECT 1 FROM silver.pessoa_atributo_observacao a WHERE a.pessoa_observacao_id=s.source_obs_id AND a.atributo_codigo='ENDERECO_CASA_ABRIGO_SIGILOSA') THEN 1 ELSE 0 END src_shelter,
      CASE WHEN EXISTS(SELECT 1 FROM gold.pessoa g WHERE g.pessoa_uuid=s.candidate_uuid AND g.cpf IS NOT NULL) THEN 1 ELSE 0 END cand_cpf,
      CASE WHEN EXISTS(SELECT 1 FROM identidade.v_vinculo_corrente vc JOIN silver.pessoa_identificador_observacao i ON i.pessoa_observacao_id=vc.pessoa_observacao_id WHERE vc.pessoa_uuid=s.candidate_uuid AND vc.status='RESOLVIDO' AND i.tipo_identificador_codigo='CNS' AND i.status_validacao<>'INVALIDO') THEN 1 ELSE 0 END cand_cns,
      CASE WHEN EXISTS(SELECT 1 FROM identidade.v_vinculo_corrente vc JOIN silver.pessoa_identificador_observacao i ON i.pessoa_observacao_id=vc.pessoa_observacao_id WHERE vc.pessoa_uuid=s.candidate_uuid AND vc.status='RESOLVIDO' AND i.tipo_identificador_codigo='RG' AND i.status_validacao<>'INVALIDO') THEN 1 ELSE 0 END cand_rg,
      CASE WHEN EXISTS(SELECT 1 FROM identidade.v_vinculo_corrente vc JOIN silver.pessoa_identificador_observacao i ON i.pessoa_observacao_id=vc.pessoa_observacao_id WHERE vc.pessoa_uuid=s.candidate_uuid AND vc.status='RESOLVIDO' AND i.tipo_identificador_codigo='UUID_JORNADA' AND i.status_validacao<>'INVALIDO') THEN 1 ELSE 0 END cand_uuid,
      CASE WHEN EXISTS(SELECT 1 FROM identidade.blocking_chave k WHERE k.pessoa_uuid=s.candidate_uuid AND k.normalizacao_versao=N'$normalizationVersion' AND k.projection_schema_version=N'$projectionSchema' AND k.projection_fingerprint_sha256=N'$projectionFingerprint' AND k.atributo=N'telefone_contato__canonical') THEN 1 ELSE 0 END cand_phone,
      CASE WHEN EXISTS(SELECT 1 FROM identidade.blocking_chave k WHERE k.pessoa_uuid=s.candidate_uuid AND k.normalizacao_versao=N'$normalizationVersion' AND k.projection_schema_version=N'$projectionSchema' AND k.projection_fingerprint_sha256=N'$projectionFingerprint' AND k.atributo=N'email_contato__canonical') THEN 1 ELSE 0 END cand_email,
      CASE WHEN EXISTS(SELECT 1 FROM identidade.blocking_chave k WHERE k.pessoa_uuid=s.candidate_uuid AND k.normalizacao_versao=N'$normalizationVersion' AND k.projection_schema_version=N'$projectionSchema' AND k.projection_fingerprint_sha256=N'$projectionFingerprint' AND k.atributo=N'nome_social__normalized') THEN 1 ELSE 0 END cand_social,
      CASE WHEN s.source_cpf IS NOT NULL AND EXISTS(SELECT 1 FROM gold.pessoa g WHERE g.pessoa_uuid=s.candidate_uuid AND g.cpf=s.source_cpf) THEN 1 ELSE 0 END exact_cpf,
      CASE WHEN EXISTS(SELECT 1 FROM silver.pessoa_identificador_observacao src JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_uuid=s.candidate_uuid AND vc.status='RESOLVIDO' JOIN silver.pessoa_identificador_observacao cand ON cand.pessoa_observacao_id=vc.pessoa_observacao_id AND cand.tipo_identificador_codigo=src.tipo_identificador_codigo AND cand.namespace_codigo=src.namespace_codigo AND cand.valor_normalizado=src.valor_normalizado WHERE src.pessoa_observacao_id=s.source_obs_id AND src.tipo_identificador_codigo='CNS' AND src.status_validacao<>'INVALIDO' AND cand.status_validacao<>'INVALIDO') THEN 1 ELSE 0 END exact_cns,
      CASE WHEN EXISTS(SELECT 1 FROM silver.pessoa_identificador_observacao src JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_uuid=s.candidate_uuid AND vc.status='RESOLVIDO' JOIN silver.pessoa_identificador_observacao cand ON cand.pessoa_observacao_id=vc.pessoa_observacao_id AND cand.tipo_identificador_codigo=src.tipo_identificador_codigo AND cand.namespace_codigo=src.namespace_codigo AND cand.valor_normalizado=src.valor_normalizado WHERE src.pessoa_observacao_id=s.source_obs_id AND src.tipo_identificador_codigo='RG' AND src.status_validacao<>'INVALIDO' AND cand.status_validacao<>'INVALIDO') THEN 1 ELSE 0 END exact_rg,
      CASE WHEN EXISTS(SELECT 1 FROM silver.pessoa_identificador_observacao src JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_uuid=s.candidate_uuid AND vc.status='RESOLVIDO' JOIN silver.pessoa_identificador_observacao cand ON cand.pessoa_observacao_id=vc.pessoa_observacao_id AND cand.tipo_identificador_codigo=src.tipo_identificador_codigo AND cand.namespace_codigo=src.namespace_codigo AND cand.valor_normalizado=src.valor_normalizado WHERE src.pessoa_observacao_id=s.source_obs_id AND src.tipo_identificador_codigo='UUID_JORNADA' AND src.status_validacao<>'INVALIDO' AND cand.status_validacao<>'INVALIDO') THEN 1 ELSE 0 END exact_uuid,
      CASE WHEN EXISTS(SELECT 1 FROM silver.pessoa_atributo_observacao src JOIN identidade.blocking_chave k ON k.pessoa_uuid=s.candidate_uuid AND k.normalizacao_versao=N'$normalizationVersion' AND k.projection_schema_version=N'$projectionSchema' AND k.projection_fingerprint_sha256=N'$projectionFingerprint' AND k.atributo=N'telefone_contato__canonical' AND k.valor_normalizado=src.atributo_instancia_chave WHERE src.pessoa_observacao_id=s.source_obs_id AND src.atributo_codigo='TELEFONE_CONTATO') THEN 1 ELSE 0 END exact_phone,
      CASE WHEN EXISTS(SELECT 1 FROM silver.pessoa_atributo_observacao src JOIN identidade.blocking_chave k ON k.pessoa_uuid=s.candidate_uuid AND k.normalizacao_versao=N'$normalizationVersion' AND k.projection_schema_version=N'$projectionSchema' AND k.projection_fingerprint_sha256=N'$projectionFingerprint' AND k.atributo=N'email_contato__canonical' AND k.valor_normalizado=src.atributo_instancia_chave WHERE src.pessoa_observacao_id=s.source_obs_id AND src.atributo_codigo='EMAIL_CONTATO') THEN 1 ELSE 0 END exact_email
    FROM segmented s
)
SELECT CONCAT(segment,'|',COUNT_BIG(*),'|',
  SUM(src_cpf),'|',SUM(cand_cpf),'|',SUM(exact_cpf),'|',
  SUM(src_cns),'|',SUM(cand_cns),'|',SUM(exact_cns),'|',
  SUM(src_rg),'|',SUM(cand_rg),'|',SUM(exact_rg),'|',
  SUM(src_uuid),'|',SUM(cand_uuid),'|',SUM(exact_uuid),'|',
  SUM(src_phone),'|',SUM(cand_phone),'|',SUM(exact_phone),'|',
  SUM(src_email),'|',SUM(cand_email),'|',SUM(exact_email),'|',
  SUM(src_social),'|',SUM(cand_social),'|',
  SUM(src_address),'|',SUM(src_territory),'|',SUM(src_shelter))
FROM flags GROUP BY segment
ORDER BY CASE segment WHEN N'ALL' THEN 1 WHEN N'POS_ALL' THEN 2 WHEN N'NEG_ALL' THEN 3 WHEN N'POS_EXACT' THEN 4 WHEN N'NEG_HARD_HOMONYM' THEN 5 ELSE 6 END;
"@)

$coverage=@(
    foreach ($line in $coverageLines) {
        $p=$line.Split('|')
        if ($p.Count -ne 25) { throw "Linha de cobertura inválida ($($p.Count) campos): $line" }
        [ordered]@{
            segment=$p[0]; total=[int]$p[1]
            cpf=[ordered]@{sourcePresent=[int]$p[2];candidatePresent=[int]$p[3];exactAgreement=[int]$p[4]}
            cns=[ordered]@{sourcePresent=[int]$p[5];candidatePresent=[int]$p[6];exactAgreement=[int]$p[7]}
            rg=[ordered]@{sourcePresent=[int]$p[8];candidatePresent=[int]$p[9];exactAgreement=[int]$p[10]}
            uuidJornada=[ordered]@{sourcePresent=[int]$p[11];candidatePresent=[int]$p[12];exactAgreement=[int]$p[13]}
            telefoneContato=[ordered]@{sourcePresent=[int]$p[14];candidateProjected=[int]$p[15];exactCanonicalAgreement=[int]$p[16]}
            emailContato=[ordered]@{sourcePresent=[int]$p[17];candidateProjected=[int]$p[18];exactCanonicalAgreement=[int]$p[19]}
            nomeSocial=[ordered]@{sourcePresent=[int]$p[20];candidateProjected=[int]$p[21];exactAgreement=$null}
            ineligibleAttributes=[ordered]@{enderecoResidencialSourcePresent=[int]$p[22];referenciaTerritorialSourcePresent=[int]$p[23];enderecoCasaAbrigoSigilosaSourcePresent=[int]$p[24]}
        }
    }
)

$all=@($coverage | Where-Object { $_['segment'] -eq 'ALL' })
$posExact=@($coverage | Where-Object { $_['segment'] -eq 'POS_EXACT' })
$hard=@($coverage | Where-Object { $_['segment'] -eq 'NEG_HARD_HOMONYM' })
if ($all.Count -ne 1 -or $all[0]['total'] -ne 80) { throw 'Cobertura ALL inválida; esperado 80.' }
if ($posExact.Count -ne 1 -or $posExact[0]['total'] -ne 8) { throw 'Cobertura POS_EXACT inválida; esperado 8.' }
if ($hard.Count -ne 1 -or $hard[0]['total'] -ne 10) { throw 'Cobertura NEG_HARD_HOMONYM inválida; esperado 10.' }

$report=[ordered]@{
    generatedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
    purpose='READ_ONLY_ADDITIONAL_IDENTITY_EVIDENCE_READINESS'
    changesPolicy=$false
    changesScoring=$false
    changesThresholds=$false
    model=[ordered]@{modelId=$activeModelId;modelVersion=$modelVersion;algorithmVersion=$algorithmVersion;runId=$runId}
    projection=[ordered]@{normalizationVersion=$normalizationVersion;schemaVersion=$projectionSchema;fingerprintSha256=$projectionFingerprint}
    currentProbabilisticScore=[ordered]@{
        fields=@('NOME','NOME_MAE','DATA_NASCIMENTO')
        note='O scorer V6 recebe LinkageCandidate apenas com nome, data de nascimento e nome da mãe. Telefone, e-mail e nome social podem participar do blocking/projeção, mas não acrescentam LLR ao score atual.'
    }
    evidenceCatalog=@(
        [ordered]@{code='CPF';kind='IDENTIFIER';currentRole='DETERMINISTIC_EXTERNAL_ANCHOR';probabilisticScore=$false},
        [ordered]@{code='UUID_JORNADA';kind='IDENTIFIER';currentRole='INTERNAL_FEEDBACK';probabilisticScore=$false},
        [ordered]@{code='CODIGO_BASE_ORIGEM';kind='IDENTIFIER';currentRole='CONDITIONAL_INTRA_NAMESPACE';probabilisticScore=$false},
        [ordered]@{code='CNS';kind='IDENTIFIER';currentRole='CONDITIONAL_EXTERNAL_IDENTIFIER';probabilisticScore=$false},
        [ordered]@{code='RG';kind='IDENTIFIER';currentRole='CONDITIONAL_EXTERNAL_IDENTIFIER';probabilisticScore=$false},
        [ordered]@{code='TELEFONE_CONTATO';kind='TRANSVERSAL';currentRole='BLOCKING_ELIGIBLE_VERSIONED_ALIAS';probabilisticScore=$false;comparison='TELEFONE_BR_CANONICO_V2'},
        [ordered]@{code='EMAIL_CONTATO';kind='TRANSVERSAL';currentRole='BLOCKING_ELIGIBLE_VERSIONED_ALIAS';probabilisticScore=$false;comparison='EMAIL_CANONICO_V2'},
        [ordered]@{code='NOME_SOCIAL';kind='TRANSVERSAL';currentRole='BLOCKING_ELIGIBLE_VERSIONED_ALIAS';probabilisticScore=$false;comparison='RUNTIME_PERSON_NAME_PROJECTION_REQUIRED'},
        [ordered]@{code='ENDERECO_RESIDENCIAL';kind='TRANSVERSAL';currentRole='INELIGIBLE_FOR_RESOLUTION';probabilisticScore=$false},
        [ordered]@{code='REFERENCIA_TERRITORIAL';kind='TRANSVERSAL';currentRole='INELIGIBLE_FOR_RESOLUTION';probabilisticScore=$false},
        [ordered]@{code='ENDERECO_CASA_ABRIGO_SIGILOSA';kind='TRANSVERSAL';currentRole='INELIGIBLE_FOR_RESOLUTION';probabilisticScore=$false}
    )
    validationCoverage=$coverage
    conclusions=@(
        'O corpus DEV mede disponibilidade de evidência adicional; não estima prevalência municipal nem desempenho em produção.',
        'Sem cobertura comparável em POS_EXACT e NEG_HARD_HOMONYM, não é possível medir ganho discriminativo de uma evidência neste corpus.',
        'Telefone/e-mail/nome social já têm superfície de blocking, mas o score V6 atual não os utiliza como LLR.',
        'CNS/RG exigem validação e governança antes de qualquer mudança de papel.',
        'Endereço residencial, referência territorial e endereço de casa-abrigo permanecem fora da resolução de identidade.'
    )
    recommendedNextGate='Criar cenários read-only com evidência independente somente após medir/garantir cobertura; calibrar m/u separado antes de qualquer alteração operacional do score.'
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $ReportPath -Encoding UTF8

Write-Host ''
Write-Host '=== READINESS DE EVIDÊNCIA ADICIONAL DO LINKAGE (READ-ONLY) ==='
Write-Host "Modelo: v$modelVersion / $activeModelId / $algorithmVersion"
Write-Host "Run: $runId"
foreach ($row in $coverage) {
    $segment=$row['segment']; $total=$row['total']
    $cpf=$row['cpf']; $cns=$row['cns']; $rg=$row['rg']; $uuid=$row['uuidJornada']
    $phone=$row['telefoneContato']; $email=$row['emailContato']; $social=$row['nomeSocial']
    Write-Host "$segment total=$total CPF=$($cpf['sourcePresent'])/$($cpf['candidatePresent']) CNS=$($cns['sourcePresent'])/$($cns['candidatePresent']) RG=$($rg['sourcePresent'])/$($rg['candidatePresent']) UUID_JORNADA=$($uuid['sourcePresent'])/$($uuid['candidatePresent']) telefone=$($phone['sourcePresent'])/$($phone['candidateProjected']) email=$($email['sourcePresent'])/$($email['candidateProjected']) nome_social=$($social['sourcePresent'])/$($social['candidateProjected'])"
}
Write-Host 'Score probabilístico atual: NOME + NOME_MAE + DATA_NASCIMENTO.'
Write-Host 'Telefone/e-mail/nome social: blocking/projeção sim; LLR V6 não.'
Write-Host 'Endereço residencial/referência territorial/casa-abrigo: fora da resolução.'
Write-Host "Relatório: $ReportPath"
Write-Host ''
Write-Host 'LINKAGE ADDITIONAL EVIDENCE READINESS: OK'
