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
    '017a20967264e5de2351de3c4b1ebe0ef98f0fbe0d16d92da392ee63176a82fd',
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

/*
  Quatro caminhos de decisão, todos determinísticos e descartáveis. O corte
  source_as_of=2099 isola exatamente estas quatro observações em um único
  RunAsync. As referências candidatas são estabelecidas por vínculos
  governados fora desse corte; nenhum parâmetro do modelo é alterado.
*/
DECLARE @system bigint,@base bigint,@manager bigint,@batch uniqueidentifier;
SELECT TOP(1)
    @system=o.sistema_origem_id,@base=o.base_pessoa_origem_id,
    @manager=p.gestor_id,@batch=p.lote_id
FROM silver.pessoa_observacao p
JOIN silver.pessoa_origem o ON o.pessoa_origem_id=p.pessoa_origem_id
WHERE o.base_pessoa_origem_id IS NOT NULL
ORDER BY p.pessoa_observacao_id;
IF @system IS NULL OR @base IS NULL OR @manager IS NULL OR @batch IS NULL
    THROW 52981,'V8_OPERATIONAL_FIXTURE_SOURCE_CONTEXT_MISSING',1;

DECLARE @cases TABLE(
    scenario nvarchar(30) PRIMARY KEY,
    code nvarchar(255) NOT NULL UNIQUE,
    full_name nvarchar(500) NOT NULL,
    mother_name nvarchar(500) NOT NULL,
    birth date NOT NULL,
    initial_uuid uniqueidentifier NOT NULL UNIQUE,
    source_id bigint NULL,
    observation_id bigint NULL);
INSERT @cases(scenario,code,full_name,mother_name,birth,initial_uuid)
VALUES
 (N'RESOLVED',N'V8-OP-RESOLVED',N'V8 ALFA RESOLVIDO',N'MAE V8 ALFA','1980-01-01','88110000-0000-4000-8000-000000000001'),
 (N'CONFLICT',N'V8-OP-CONFLICT',N'V8 BETA CONFLITO',N'MAE V8 BETA','1981-02-02','88110000-0000-4000-8000-000000000002'),
 (N'BELOW_THRESHOLD',N'V8-OP-BELOW',N'V8 GAMA ABAIXO',N'MAE V8 GAMA','1982-03-03','88110000-0000-4000-8000-000000000003'),
 (N'NO_CANDIDATE',N'V8-OP-NONE',N'V8 DELTA SEM CANDIDATO',N'MAE V8 DELTA','1983-04-04','88110000-0000-4000-8000-000000000004');

INSERT silver.pessoa_origem(
    sistema_origem_id,codigo_pessoa_origem,base_pessoa_origem_id)
SELECT @system,code,@base FROM @cases;
UPDATE c SET source_id=o.pessoa_origem_id
FROM @cases c JOIN silver.pessoa_origem o
  ON o.sistema_origem_id=@system AND o.codigo_pessoa_origem=c.code;

INSERT silver.pessoa_observacao(
    pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,
    conteudo_hash,cpf,cpf_ausente_motivo,nome_completo,nome_cmp,
    data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
SELECT source_id,@batch,@manager,code,1,
       CASE scenario
         WHEN N'RESOLVED' THEN REPLICATE('1',64)
         WHEN N'CONFLICT' THEN REPLICATE('2',64)
         WHEN N'BELOW_THRESHOLD' THEN REPLICATE('3',64)
         ELSE REPLICATE('4',64) END,
       NULL,N'SEM_CPF',full_name,full_name,birth,mother_name,mother_name,
       CONVERT(datetimeoffset(7),'2099-01-01T00:00:00+00:00')
FROM @cases;
UPDATE c SET observation_id=p.pessoa_observacao_id
FROM @cases c JOIN silver.pessoa_observacao p
  ON p.pessoa_origem_id=c.source_id AND p.versao_interna=1;

INSERT identidade.pessoa(pessoa_uuid,status)
SELECT initial_uuid,N'ATIVO' FROM @cases;
INSERT identidade.pessoa_origem_progressiva(
    pessoa_origem_id,initial_uuid,estado,versao,criado_em,atualizado_em)
SELECT source_id,initial_uuid,'PROVISORIA',0,SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET()
FROM @cases;
INSERT identidade.pessoa_origem_progressiva_evento(
    evento_id,pessoa_origem_id,versao,tipo,estado,ocorrido_em)
SELECT NEWID(),source_id,0,'CRIACAO','PROVISORIA',SYSDATETIMEOFFSET()
FROM @cases;
INSERT gold.pessoa(
    pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,
    fontes_distintas,estado_concordancia,estado_identidade,completude_nucleo,atualizado_em)
SELECT initial_uuid,NULL,N'SEM_CPF',full_name,birth,mother_name,
       1,N'BASELINE_FONTE_UNICA',N'PROVISORIA',N'COMPLETO',SYSDATETIMEOFFSET()
FROM @cases;

DECLARE @candidates TABLE(
    scenario nvarchar(30) NOT NULL,
    ordinal int NOT NULL,
    person_uuid uniqueidentifier NOT NULL PRIMARY KEY,
    full_name nvarchar(500) NOT NULL,
    mother_name nvarchar(500) NOT NULL,
    birth date NOT NULL);
INSERT @candidates(scenario,ordinal,person_uuid,full_name,mother_name,birth)
VALUES
 (N'RESOLVED',1,'88220000-0000-4000-8000-000000000001',N'V8 ALFA RESOLVIDO',N'MAE V8 ALFA','1980-01-01'),
 (N'CONFLICT',2,'88220000-0000-4000-8000-000000000002',N'V8 BETA CONFLITO',N'MAE V8 BETA','1981-02-02'),
 (N'CONFLICT',3,'88220000-0000-4000-8000-000000000003',N'V8 BETA CONFLITO',N'MAE V8 BETA','1981-02-02'),
 (N'BELOW_THRESHOLD',4,'88220000-0000-4000-8000-000000000004',N'PESSOA TOTALMENTE DIVERGENTE',N'MAE TOTALMENTE DIVERGENTE','1982-03-03');
INSERT identidade.pessoa(pessoa_uuid,status)
SELECT person_uuid,N'ATIVO' FROM @candidates;
INSERT gold.pessoa(
    pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,
    fontes_distintas,estado_concordancia,estado_identidade,completude_nucleo,atualizado_em)
SELECT person_uuid,NULL,N'SEM_CPF',full_name,birth,mother_name,
       1,N'BASELINE_FONTE_UNICA',N'REFERENCIA',N'COMPLETO',SYSDATETIMEOFFSET()
FROM @candidates;

-- A mesma chave física admite o candidato divergente do caso abaixo do
-- limiar; o score, não o blocking, deve produzir essa decisão.
INSERT identidade.blocking_chave(
    pessoa_uuid,normalizacao_versao,atributo,valor_normalizado,semantica_temporal)
SELECT d.person_uuid,N'IDENTITY_NORMALIZATION_V1',N'name_full',c.full_name,N'VERSIONED_ALIAS'
FROM @candidates d JOIN @cases c ON c.scenario=d.scenario;

-- Referências candidatas estabelecidas, mas fora do corte futuro do run.
INSERT silver.pessoa_origem(
    sistema_origem_id,codigo_pessoa_origem,base_pessoa_origem_id)
VALUES(@system,N'V8-OP-CANDIDATE-ANCHORS',@base);
DECLARE @anchor_source bigint=SCOPE_IDENTITY();
INSERT silver.pessoa_observacao(
    pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,
    conteudo_hash,cpf,cpf_ausente_motivo,nome_completo,nome_cmp,
    data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
SELECT @anchor_source,@batch,@manager,N'V8-OP-CANDIDATE-ANCHORS',ordinal,
       REPLICATE(CONVERT(char(1),ordinal+4),64),NULL,N'SEM_CPF',full_name,full_name,
       birth,mother_name,mother_name,CONVERT(datetimeoffset(7),'2000-01-01T00:00:00+00:00')
FROM @candidates;
INSERT identidade.vinculo_fonte(
    pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,
    modelo_id,ativo,resolvido_em,motivo)
SELECT p.pessoa_observacao_id,c.person_uuid,N'CORRECAO_GOVERNADA',NULL,N'RESOLVIDO',
       NULL,1,SYSDATETIMEOFFSET(),N'V8_OPERATIONAL_FIXED_REFERENCE'
FROM @candidates c
JOIN silver.pessoa_observacao p
  ON p.pessoa_origem_id=@anchor_source AND p.versao_interna=c.ordinal;

IF (SELECT COUNT_BIG(*) FROM @cases WHERE source_id IS NOT NULL AND observation_id IS NOT NULL)<>4
   OR (SELECT COUNT_BIG(*) FROM @candidates)<>4
   OR (SELECT COUNT_BIG(*) FROM identidade.blocking_chave b
       JOIN @candidates c ON c.person_uuid=b.pessoa_uuid
       WHERE b.atributo=N'name_full' AND b.vigencia_fim IS NULL)<>4
   OR (SELECT COUNT_BIG(*) FROM silver.pessoa_observacao
       WHERE source_as_of>=CONVERT(datetimeoffset(7),'2099-01-01T00:00:00+00:00'))<>4
    THROW 52982,'V8_OPERATIONAL_FOUR_SCENARIO_FIXTURE_INVALID',1;

PRINT 'V8_OPERATIONAL_FIXED_MODEL_AND_FOUR_SCENARIOS_READY_NO_TRAIN_VALIDATION_ACTIVATION';
GO
