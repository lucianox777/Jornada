SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @model uniqueidentifier='88000000-0000-4000-8000-000000008008';
DECLARE @version int=8008;
DECLARE @run uniqueidentifier;
DECLARE @stage nvarchar(40)=COALESCE((
    SELECT CONVERT(nvarchar(40),value)
    FROM sys.extended_properties
    WHERE class=0 AND name=N'Jornada.LinkagePublicationScale'),N'BASELINE');

IF @stage NOT IN(N'BASELINE',N'20261126_V3')
    THROW 52974,'V8_OPERATIONAL_RECEIPT_SCHEMA_STAGE_INVALID',1;

IF (SELECT COUNT_BIG(*) FROM identidade.linkage_run
    WHERE modelo_id=@model AND modelo_versao=@version
      AND solicitado_por=N'CI_V8_OPERATIONAL_REGRESSION')<>1
    THROW 52975,'V8_OPERATIONAL_RECEIPT_RUN_CARDINALITY',1;

SELECT @run=linkage_run_id
FROM identidade.linkage_run
WHERE modelo_id=@model AND modelo_versao=@version
  AND solicitado_por=N'CI_V8_OPERATIONAL_REGRESSION';

IF NOT EXISTS(
    SELECT 1 FROM identidade.linkage_run
    WHERE linkage_run_id=@run AND tipo_run=N'ON_DEMAND' AND status=N'PUBLICADO'
      AND pessoa_observacao_id_filtro IS NULL
      AND desde_filtro=CONVERT(datetimeoffset(7),'2099-01-01T00:00:00+00:00')
      AND limite_solicitado=4
      AND registros_elegiveis=4 AND avaliados=4
      AND resolvidos=1 AND conflitos=1 AND nao_resolvidos=2 AND sem_candidato_no_bloco=1
      AND avaliados=resolvidos+nao_resolvidos+conflitos)
    THROW 52976,'V8_OPERATIONAL_RECEIPT_RUN_NOT_PUBLISHED',1;

IF NOT EXISTS(
    SELECT 1
    FROM identidade.modelo_linkage m
    JOIN identidade.linkage_ruleset r ON r.modelo_id=m.modelo_id
    WHERE m.modelo_id=@model AND m.versao=@version AND m.status=N'INATIVO'
      AND m.algoritmo_versao=N'FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8'
      AND r.ruleset_versao=N'V8_OPERATIONAL_REGRESSION_RULESET_V1'
      AND r.fingerprint_sha256='017a20967264e5de2351de3c4b1ebe0ef98f0fbe0d16d92da392ee63176a82fd'
      AND r.projection_schema_version=N'PERSON_RESOLUTION_PROJECTION_V2'
      AND r.projection_fingerprint_sha256='d186f28c51e18802f7c2df5b8b192b6278d28874833c8d824d483608aa3abbb4')
    THROW 52977,'V8_OPERATIONAL_RECEIPT_MODEL_NOT_FROZEN',1;

IF (SELECT COUNT_BIG(*) FROM auditoria.modelo_linkage_estado_evento
    WHERE modelo_id=@model)<>2
   OR NOT EXISTS(
       SELECT 1 FROM auditoria.modelo_linkage_estado_evento
       WHERE modelo_id=@model AND status_anterior IS NULL
         AND status_novo=N'RASCUNHO' AND operacao_codigo=N'INSERT_EXISTING_STATE')
   OR NOT EXISTS(
       SELECT 1 FROM auditoria.modelo_linkage_estado_evento
       WHERE modelo_id=@model AND status_anterior=N'RASCUNHO'
         AND status_novo=N'INATIVO' AND operacao_codigo=N'STATUS_TRANSITION')
   OR EXISTS(
       SELECT 1 FROM auditoria.modelo_linkage_estado_evento
       WHERE modelo_id=@model
         AND operacao_codigo IN(N'GENERATE_DRAFT_START',N'GENERATE_DRAFT_COMPLETE',
             N'GENERATE_DRAFT_FAILED',N'VALIDATE',N'ACTIVATE',N'ACTIVATE_SUPERSEDED'))
    THROW 52978,'V8_OPERATIONAL_FIXTURE_LIFECYCLE_OPERATION_FORBIDDEN',1;

DECLARE @highWatermark bigint=(
    SELECT pessoa_observacao_id_high_watermark
    FROM identidade.linkage_run WHERE linkage_run_id=@run);
DECLARE @expectedUniverse nvarchar(255)=CONCAT(
    N'LINKAGE_RUN:',CONVERT(nvarchar(36),@run),
    N';HW:',CONVERT(nvarchar(30),@highWatermark),N';ELIGIVEIS:4');

IF (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado WHERE linkage_run_id=@run)<>4
   OR EXISTS(
       SELECT 1 FROM identidade.linkage_resultado
       WHERE linkage_run_id=@run
         AND (universo_referencia COLLATE Latin1_General_100_BIN2<>
              @expectedUniverse COLLATE Latin1_General_100_BIN2
              OR politica_publicacao_versao<>N'LINKAGE_PROGRESSIVE_PUBLICATION_V1'
              OR publicado_em IS NULL))
    THROW 52979,'V8_OPERATIONAL_RECEIPT_UNIVERSE_INVALID',1;

DECLARE @scenarios TABLE(
    scenario nvarchar(30) NOT NULL PRIMARY KEY,
    status nvarchar(30) NOT NULL,
    reason nvarchar(500) NULL,
    resolved_uuid uniqueidentifier NULL,
    best_uuid uniqueidentifier NULL,
    best_score decimal(9,8) NOT NULL,
    second_uuid uniqueidentifier NULL,
    second_score decimal(9,8) NULL,
    margin decimal(30,12) NULL,
    publication_result nvarchar(30) NULL,
    publication_status nvarchar(30) NULL,
    published_uuid uniqueidentifier NULL);
INSERT @scenarios
SELECT CASE o.codigo_pessoa_origem
      WHEN N'V8-OP-RESOLVED' THEN N'RESOLVED'
      WHEN N'V8-OP-CONFLICT' THEN N'CONFLICT'
      WHEN N'V8-OP-BELOW' THEN N'BELOW_THRESHOLD'
      WHEN N'V8-OP-NONE' THEN N'NO_CANDIDATE' END,
      r.status,r.motivo,r.pessoa_uuid_resolvido,r.melhor_candidato_uuid,r.score_melhor,
      r.segundo_candidato_uuid,r.score_segundo,r.margem,
      r.resultado_publicacao,r.status_publicacao,r.pessoa_uuid_publicado
    FROM identidade.linkage_resultado r
    JOIN silver.pessoa_observacao p ON p.pessoa_observacao_id=r.pessoa_observacao_id
    JOIN silver.pessoa_origem o ON o.pessoa_origem_id=p.pessoa_origem_id
    WHERE r.linkage_run_id=@run;
IF (SELECT COUNT_BIG(*) FROM @scenarios)<>4
   OR NOT EXISTS(SELECT 1 FROM @scenarios WHERE scenario=N'RESOLVED'
       AND status=N'RESOLVIDO' AND resolved_uuid=best_uuid
       AND second_uuid IS NULL AND publication_result=N'ASSOCIACAO_EXISTENTE'
       AND publication_status=N'RESOLVIDO' AND published_uuid=resolved_uuid)
   OR NOT EXISTS(SELECT 1 FROM @scenarios WHERE scenario=N'CONFLICT'
       AND status=N'CONFLITO' AND resolved_uuid IS NULL
       AND best_uuid IS NOT NULL AND second_uuid IS NOT NULL
       AND best_score=second_score AND margin=0
       AND publication_result=N'INDEFINIDA' AND publication_status=N'CONFLITO'
       AND published_uuid IS NULL)
   OR NOT EXISTS(SELECT 1 FROM @scenarios WHERE scenario=N'BELOW_THRESHOLD'
       AND status=N'NAO_RESOLVIDO' AND reason=N'ABAIXO_T_LINKAGE'
       AND resolved_uuid IS NULL AND best_uuid IS NOT NULL
       AND publication_result=N'INDEFINIDA' AND publication_status=N'NAO_RESOLVIDO'
       AND published_uuid IS NULL)
   OR NOT EXISTS(SELECT 1 FROM @scenarios WHERE scenario=N'NO_CANDIDATE'
       AND status=N'NAO_RESOLVIDO' AND reason=N'SEM_CANDIDATO_NO_RULESET_BLOCKING'
       AND resolved_uuid IS NULL AND best_uuid IS NULL
       AND second_uuid IS NULL AND publication_result=N'NOVA_IDENTIDADE'
       AND publication_status=N'RESOLVIDO' AND published_uuid IS NOT NULL)
    THROW 52983,'V8_OPERATIONAL_RECEIPT_SCENARIO_COVERAGE_INVALID',1;

DECLARE @receipt nvarchar(max)=(
    SELECT
      N'V8_OPERATIONAL_REGRESSION_PRIVATE_V2' AS [schema],
      @stage AS schemaStage,
      N'Jornada.Linkage.Runner.Program/ProbabilisticLinkageBatchRunner.RunAsync' AS entrypoint,
      CAST(1 AS bit) AS publish,
      N'NO_TRAIN_NO_VALIDATION_NO_ACTIVATION' AS fixtureLifecycle,
      LOWER(CONVERT(varchar(36),@run)) AS runId,
      JSON_QUERY((
        SELECT LOWER(CONVERT(varchar(36),m.modelo_id)) AS modelId,m.versao AS version,
               m.status,m.algoritmo_versao AS algorithmVersion,
               r.ruleset_versao AS ruleSetVersion,
               r.fingerprint_sha256 AS ruleSetFingerprintSha256,
               r.projection_schema_version AS projectionSchemaVersion,
               r.projection_fingerprint_sha256 AS projectionFingerprintSha256
        FROM identidade.modelo_linkage m
        JOIN identidade.linkage_ruleset r ON r.modelo_id=m.modelo_id
        WHERE m.modelo_id=@model
        FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)) AS model,
      JSON_QUERY((
        SELECT lr.tipo_run AS mode,lr.status,lr.pessoa_observacao_id_filtro AS observationId,
               lr.pessoa_observacao_id_high_watermark AS highWatermark,
               lr.registros_elegiveis AS eligible,lr.avaliados AS evaluated,
               lr.resolvidos AS resolved,lr.nao_resolvidos AS unresolved,
               lr.conflitos AS conflicts,lr.sem_candidato_no_bloco AS noCandidateInBlock,
               lr.batch_size AS batchSize,lr.max_parallelism AS maxParallelism,
               lr.solicitado_por AS requestedBy,lr.motivo AS reason
        FROM identidade.linkage_run lr WHERE lr.linkage_run_id=@run
        FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)) AS run,
      JSON_QUERY((
        SELECT CASE o.codigo_pessoa_origem
                 WHEN N'V8-OP-RESOLVED' THEN N'RESOLVED'
                 WHEN N'V8-OP-CONFLICT' THEN N'CONFLICT'
                 WHEN N'V8-OP-BELOW' THEN N'BELOW_THRESHOLD'
                 WHEN N'V8-OP-NONE' THEN N'NO_CANDIDATE' END AS scenario,
               r.pessoa_observacao_id AS observationId,
               LOWER(CONVERT(varchar(36),r.pessoa_uuid_resolvido)) AS resolvedUuid,
               LOWER(CONVERT(varchar(36),r.melhor_candidato_uuid)) AS bestUuid,
               CONVERT(varchar(40),r.score_melhor) AS bestScore,
               LOWER(CONVERT(varchar(36),r.segundo_candidato_uuid)) AS secondUuid,
               CONVERT(varchar(40),r.score_segundo) AS secondScore,
               CONVERT(varchar(40),r.margem) AS margin,
               r.status,r.motivo AS reason,r.resultado_publicacao AS publicationResult,
               LOWER(CONVERT(varchar(36),r.pessoa_uuid_publicado)) AS publishedUuid,
               r.status_publicacao AS publicationStatus,
               r.motivo_publicacao AS publicationReason,
               r.pessoa_origem_id_publicado AS publishedOriginId,
               r.progressiva_versao AS progressiveVersion,
               r.politica_publicacao_versao AS publicationPolicy,
               r.universo_referencia AS universeReference
        FROM identidade.linkage_resultado r
        JOIN silver.pessoa_observacao p ON p.pessoa_observacao_id=r.pessoa_observacao_id
        JOIN silver.pessoa_origem o ON o.pessoa_origem_id=p.pessoa_origem_id
        WHERE r.linkage_run_id=@run
        ORDER BY r.pessoa_observacao_id
        FOR JSON PATH,INCLUDE_NULL_VALUES)) AS decisions,
      JSON_QUERY((
        SELECT LOWER(CONVERT(varchar(36),g.pessoa_uuid)) AS personUuid,g.cpf,g.status_cpf AS cpfStatus,
               g.nome_completo AS fullName,g.data_nascimento AS birthDate,
               g.nome_mae AS motherName,g.fontes_distintas AS distinctSources,
               g.estado_concordancia AS concordanceState,
               g.estado_identidade AS identityState,g.completude_nucleo AS nucleusCompleteness,
               g.nome_publicacao_normalizado AS normalizedPublicationName,
               g.nome_publicacao_metodo_versao AS publicationNameMethodVersion,
               g.nome_publicacao_normalizacao_versao AS publicationNameNormalizationVersion
        FROM gold.pessoa g
        WHERE g.pessoa_uuid IN(
            SELECT x.initial_uuid FROM identidade.pessoa_origem_progressiva x
            JOIN silver.pessoa_observacao p ON p.pessoa_origem_id=x.pessoa_origem_id
            JOIN identidade.linkage_run_item i ON i.pessoa_observacao_id=p.pessoa_observacao_id
            WHERE i.linkage_run_id=@run
            UNION SELECT r.pessoa_uuid_resolvido FROM identidade.linkage_resultado r
                  WHERE r.linkage_run_id=@run AND r.pessoa_uuid_resolvido IS NOT NULL
            UNION SELECT r.melhor_candidato_uuid FROM identidade.linkage_resultado r
                  WHERE r.linkage_run_id=@run AND r.melhor_candidato_uuid IS NOT NULL
            UNION SELECT r.segundo_candidato_uuid FROM identidade.linkage_resultado r
                  WHERE r.linkage_run_id=@run AND r.segundo_candidato_uuid IS NOT NULL
            UNION SELECT r.pessoa_uuid_publicado FROM identidade.linkage_resultado r
                  WHERE r.linkage_run_id=@run AND r.pessoa_uuid_publicado IS NOT NULL)
        ORDER BY g.pessoa_uuid
        FOR JSON PATH,INCLUDE_NULL_VALUES)) AS gold,
      JSON_QUERY((
        SELECT v.pessoa_observacao_id AS observationId,
               LOWER(CONVERT(varchar(36),v.pessoa_uuid)) AS personUuid,
               v.metodo_resolucao AS method,CONVERT(varchar(40),v.score) AS score,
               v.status,v.motivo AS reason,LOWER(CONVERT(varchar(36),v.modelo_id)) AS modelId,
               CAST(CASE WHEN v.linkage_run_id=@run THEN 1 ELSE 0 END AS bit) AS exactRun
        FROM identidade.v_vinculo_corrente v
        WHERE EXISTS(SELECT 1 FROM identidade.linkage_run_item i
                     WHERE i.linkage_run_id=@run
                       AND i.pessoa_observacao_id=v.pessoa_observacao_id)
        ORDER BY v.pessoa_observacao_id,v.pessoa_uuid
        FOR JSON PATH,INCLUDE_NULL_VALUES)) AS currentLinks,
      JSON_QUERY((
        SELECT p.pessoa_origem_id AS originId,
               LOWER(CONVERT(varchar(36),p.initial_uuid)) AS initialUuid,
               LOWER(CONVERT(varchar(36),p.canonical_uuid)) AS canonicalUuid,
               LOWER(CONVERT(varchar(36),p.legacy_pessoa_uuid)) AS legacyUuid,
               p.estado AS state,p.versao AS version,
               LOWER(CONVERT(varchar(36),p.ultimo_destino_externo_uuid)) AS lastExternalTargetUuid
        FROM identidade.pessoa_origem_progressiva p
        JOIN silver.pessoa_observacao o ON o.pessoa_origem_id=p.pessoa_origem_id
        JOIN identidade.linkage_run_item i ON i.pessoa_observacao_id=o.pessoa_observacao_id
        WHERE i.linkage_run_id=@run
        ORDER BY o.pessoa_observacao_id
        FOR JSON PATH,INCLUDE_NULL_VALUES)) AS progressiveOrigins
    FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES);

IF @receipt IS NULL OR DATALENGTH(@receipt)>1048576
    THROW 52980,'V8_OPERATIONAL_RECEIPT_SIZE_INVALID',1;

SELECT CONCAT(N'V8_OPERATIONAL_PRIVATE_JSON=',@receipt);
GO
