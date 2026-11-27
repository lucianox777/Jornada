SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Fixture V8 fixa e não científica para a regressão operacional da publicação.
  Não gera, calibra, valida ou ativa modelo. A fixture nasce RASCUNHO apenas
  enquanto o contrato imutável de ruleset é anexado e passa diretamente a
  INATIVO na mesma transação. O Runner a consome somente porque a versão é
  explicitamente pinada no CLI descartável.
*/
DECLARE @model uniqueidentifier='88000000-0000-4000-8000-000000008008';
DECLARE @version int=8008;
DECLARE @algorithm nvarchar(80)=N'FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8';
DECLARE @ruleset uniqueidentifier=@model;

IF DB_NAME() NOT LIKE N'JornadaV9Dev[_]%'
   OR NOT EXISTS(
      SELECT 1 FROM sys.extended_properties
      WHERE class=0 AND name=N'Jornada.EnvironmentProfile'
        AND CONVERT(nvarchar(32),value)=N'Development')
   OR NOT EXISTS(
      SELECT 1 FROM sys.extended_properties
      WHERE class=0 AND name=N'Jornada.V9.DevAttempt')
    THROW 52971,'V8_OPERATIONAL_FIXTURE_REQUIRES_DISPOSABLE_DEV_DATABASE',1;

IF EXISTS(SELECT 1 FROM identidade.modelo_linkage WHERE modelo_id=@model OR versao=@version)
    THROW 52972,'V8_OPERATIONAL_FIXTURE_ALREADY_EXISTS',1;

BEGIN TRANSACTION;

INSERT identidade.modelo_linkage(
    modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
    deduplicacao_metodo,base_referencia,snapshot_referencia,
    registros_lidos,pessoas_unicas,gerado_em,snapshot_capturado_em,
    amostra_metodo,amostra_pool_tamanho,amostra_m_tamanho,amostra_u_tamanho)
VALUES(
    @model,@version,N'RASCUNHO',@algorithm,N'IDENTITY_NORMALIZATION_V1',
    N'FIXED_OPERATIONAL_REGRESSION_FIXTURE',N'DISPOSABLE_V9_SCALE_GOLD',
    N'GOV-V9-V8-OPERATIONAL-REGRESSION-FIXTURE-V1',
    1,1,'2026-11-26T00:00:00+00:00','2026-11-26T00:00:00',
    N'FIXED_NO_TRAIN_NO_VALIDATION_NO_ACTIVATION',1,1,1);

INSERT identidade.parametro_linkage(modelo_id,nome,valor)
VALUES
(@model,N'PRIOR_MATCH_PROBABILITY',0.01),
(@model,N'PRIOR_BLOCK_MIN',0.000001),
(@model,N'PRIOR_BLOCK_MAX',0.25),
(@model,N'T_LINKAGE',0.95),
(@model,N'CONFLICT_MARGIN',0.03),
(@model,N'CONFLICT_MARGIN_LOG_ODDS',0.03),
(@model,N'SCORING_DECISION_EVIDENCE_V6',1),
(@model,N'SCORING_MISSING_EVIDENCE_NEUTRAL_V1',1),
(@model,N'SCORING_BIRTH_SEMANTIC_EVIDENCE_V5',1),
(@model,N'SUPPORT_M_NOME_MAE_MISSING',1),
(@model,N'SUPPORT_U_NOME_MAE_MISSING',1),
(@model,N'M_NOME_EXACT',0.70),
(@model,N'M_NOME_HIGH',0.20),
(@model,N'M_NOME_MEDIUM',0.08),
(@model,N'M_NOME_LOW',0.02),
(@model,N'U_NOME_EXACT',0.01),
(@model,N'U_NOME_HIGH',0.04),
(@model,N'U_NOME_MEDIUM',0.15),
(@model,N'U_NOME_LOW',0.80),
(@model,N'M_NOME_MAE_EXACT',0.70),
(@model,N'M_NOME_MAE_HIGH',0.20),
(@model,N'M_NOME_MAE_MEDIUM',0.08),
(@model,N'M_NOME_MAE_LOW',0.02),
(@model,N'U_NOME_MAE_EXACT',0.01),
(@model,N'U_NOME_MAE_HIGH',0.04),
(@model,N'U_NOME_MAE_MEDIUM',0.15),
(@model,N'U_NOME_MAE_LOW',0.80),
(@model,N'M_NASCIMENTO_SEMANTICO_EXACT',0.142857),
(@model,N'M_NASCIMENTO_SEMANTICO_DAY_MONTH_SWAP',0.142857),
(@model,N'M_NASCIMENTO_SEMANTICO_CENTURY_SHIFT',0.142857),
(@model,N'M_NASCIMENTO_SEMANTICO_ONE_DIGIT_ERROR',0.142857),
(@model,N'M_NASCIMENTO_SEMANTICO_TWO_DIGIT_ERROR',0.142857),
(@model,N'M_NASCIMENTO_SEMANTICO_PARTIAL_COMPONENT_AGREEMENT',0.142857),
(@model,N'M_NASCIMENTO_SEMANTICO_OTHER_DISAGREEMENT',0.142857),
(@model,N'U_NASCIMENTO_SEMANTICO_EXACT',0.142857),
(@model,N'U_NASCIMENTO_SEMANTICO_DAY_MONTH_SWAP',0.142857),
(@model,N'U_NASCIMENTO_SEMANTICO_CENTURY_SHIFT',0.142857),
(@model,N'U_NASCIMENTO_SEMANTICO_ONE_DIGIT_ERROR',0.142857),
(@model,N'U_NASCIMENTO_SEMANTICO_TWO_DIGIT_ERROR',0.142857),
(@model,N'U_NASCIMENTO_SEMANTICO_PARTIAL_COMPONENT_AGREEMENT',0.142857),
(@model,N'U_NASCIMENTO_SEMANTICO_OTHER_DISAGREEMENT',0.142857),
(@model,N'SUPPORT_U_NASCIMENTO_SEMANTICO_EXACT',1),
(@model,N'SUPPORT_U_NASCIMENTO_SEMANTICO_DAY_MONTH_SWAP',1),
(@model,N'SUPPORT_U_NASCIMENTO_SEMANTICO_CENTURY_SHIFT',1),
(@model,N'SUPPORT_U_NASCIMENTO_SEMANTICO_ONE_DIGIT_ERROR',1),
(@model,N'SUPPORT_U_NASCIMENTO_SEMANTICO_TWO_DIGIT_ERROR',1),
(@model,N'SUPPORT_U_NASCIMENTO_SEMANTICO_PARTIAL_COMPONENT_AGREEMENT',1),
(@model,N'SUPPORT_U_NASCIMENTO_SEMANTICO_OTHER_DISAGREEMENT',1),
(@model,N'POOL_SUPPORT_U_NASCIMENTO_SEMANTICO_EXACT',0),
(@model,N'POOL_SUPPORT_U_NASCIMENTO_SEMANTICO_DAY_MONTH_SWAP',0),
(@model,N'POOL_SUPPORT_U_NASCIMENTO_SEMANTICO_CENTURY_SHIFT',0),
(@model,N'POOL_SUPPORT_U_NASCIMENTO_SEMANTICO_ONE_DIGIT_ERROR',0),
(@model,N'POOL_SUPPORT_U_NASCIMENTO_SEMANTICO_TWO_DIGIT_ERROR',0),
(@model,N'POOL_SUPPORT_U_NASCIMENTO_SEMANTICO_PARTIAL_COMPONENT_AGREEMENT',0),
(@model,N'POOL_SUPPORT_U_NASCIMENTO_SEMANTICO_OTHER_DISAGREEMENT',0);

INSERT identidade.linkage_ruleset(
    ruleset_id,modelo_id,ruleset_versao,algoritmo_versao,fingerprint_sha256,
    projection_schema_version,projection_fingerprint_sha256,
    ibge_source_versao,ibge_fingerprint_sha256)
VALUES(
    @ruleset,@model,N'V8_OPERATIONAL_REGRESSION_RULESET_V1',@algorithm,
    '900a6ab052f078f8422d76a297a5a9fbf6fe0003f764edc86e5d01e802991504',
    N'PERSON_RESOLUTION_PROJECTION_V2',
    'd186f28c51e18802f7c2df5b8b192b6278d28874833c8d824d483608aa3abbb4',
    NULL,NULL);

INSERT identidade.linkage_ruleset_passe(ruleset_id,passe_ordem,passe_id)
VALUES(@ruleset,0,N'full-name');

INSERT identidade.linkage_ruleset_passe_campo(
    ruleset_id,passe_ordem,campo_ordem,atributo)
VALUES(@ruleset,0,0,N'name_full');

-- Não é promoção: a fixture não passa por VALIDADO nem ATIVO. INATIVO é o
-- estado consumível explicitamente pinado e não concorre com o modelo vigente.
UPDATE identidade.modelo_linkage
SET status=N'INATIVO'
WHERE modelo_id=@model AND status=N'RASCUNHO';

IF @@ROWCOUNT<>1
    THROW 52973,'V8_OPERATIONAL_FIXTURE_INACTIVE_TRANSITION_FAILED',1;

COMMIT TRANSACTION;

IF (SELECT COUNT_BIG(*) FROM identidade.parametro_linkage WHERE modelo_id=@model)<>55
   OR NOT EXISTS(
       SELECT 1 FROM identidade.modelo_linkage
       WHERE modelo_id=@model AND versao=@version AND status=N'INATIVO'
         AND algoritmo_versao=@algorithm)
    THROW 52973,'V8_OPERATIONAL_FIXTURE_NOT_FROZEN',1;

PRINT 'V8_OPERATIONAL_FIXED_MODEL_READY_NO_TRAIN_VALIDATION_ACTIVATION';
GO
