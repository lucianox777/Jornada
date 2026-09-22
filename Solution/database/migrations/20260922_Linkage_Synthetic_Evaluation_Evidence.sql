SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Evidência sintética do Calibrador (#416)
  -----------------------------------------
  Persiste somente agregados produzidos DEPOIS do RASCUNHO.
  Identificadores de truth, atributos pessoais e linhas individuais não pertencem a este contrato.

  A presença desta evidência:
  - nunca valida nem ativa modelo;
  - nunca satisfaz a validação estatística representativa #31;
  - nunca satisfaz a conferência independente de implementação;
  - só pode ser registrada quando Jornada.EnvironmentProfile=Development.
*/

IF OBJECT_ID(N'identidade.modelo_linkage',N'U') IS NULL
   OR OBJECT_ID(N'auditoria.sp_calcular_fingerprint_modelo_linkage',N'P') IS NULL
    THROW 51910,'Evidência sintética exige modelo_linkage e fingerprint canônico instalados.',1;
GO

IF TYPE_ID(N'auditoria.linkage_avaliacao_sintetica_metrica_tvp') IS NULL
EXEC(N'
CREATE TYPE auditoria.linkage_avaliacao_sintetica_metrica_tvp AS TABLE(
    escopo NVARCHAR(80) NOT NULL,
    dimensao NVARCHAR(120) NULL,
    metrica NVARCHAR(120) NOT NULL,
    valor DECIMAL(38,16) NOT NULL,
    unidade NVARCHAR(40) NOT NULL
);');
GO

IF OBJECT_ID(N'auditoria.linkage_avaliacao_sintetica',N'U') IS NULL
CREATE TABLE auditoria.linkage_avaliacao_sintetica(
    linkage_avaliacao_sintetica_id BIGINT IDENTITY PRIMARY KEY,
    avaliacao_id UNIQUEIDENTIFIER NOT NULL,
    grupo_execucao_id UNIQUEIDENTIFIER NOT NULL,
    modelo_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.modelo_linkage(modelo_id),
    modelo_versao INT NOT NULL,
    modelo_snapshot_sha256 BINARY(32) NOT NULL,
    corpus_fingerprint_sha256 BINARY(32) NOT NULL,
    generation_manifest_sha256 BINARY(32) NOT NULL,
    bridge_manifest_sha256 BINARY(32) NOT NULL,
    observations_sha256 BINARY(32) NOT NULL,
    bridge_truth_sha256 BINARY(32) NOT NULL,
    relatorio_schema_versao NVARCHAR(120) NOT NULL,
    natureza NVARCHAR(80) NOT NULL,
    finalidade NVARCHAR(120) NOT NULL,
    gerador_versao NVARCHAR(120) NOT NULL,
    gerador_seed DECIMAL(20,0) NOT NULL,
    avaliador_versao NVARCHAR(120) NOT NULL,
    ambiente_perfil NVARCHAR(32) NOT NULL,
    status NVARCHAR(20) NOT NULL,
    ruleset_versao NVARCHAR(120) NOT NULL,
    ruleset_fingerprint_sha256 BINARY(32) NOT NULL,
    u_semantica NVARCHAR(120) NOT NULL,
    m_truth_universo NVARCHAR(220) NOT NULL,
    u_truth_universo NVARCHAR(220) NOT NULL,
    u_nome_fonte_nominal NVARCHAR(80) NOT NULL,
    u_nome_mae_fonte_nominal NVARCHAR(80) NOT NULL,
    observacoes_materializadas BIGINT NOT NULL,
    observacoes_excluidas BIGINT NOT NULL,
    report_sha256 BINARY(32) NOT NULL,
    validacao_estatistica NVARCHAR(80) NOT NULL,
    promocao_autorizada BIT NOT NULL,
    executor_aplicacao NVARCHAR(128) NOT NULL,
    executor_login NVARCHAR(256) NOT NULL,
    executor_host NVARCHAR(128) NULL,
    source_revision NVARCHAR(80) NULL,
    ocorrido_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
    CONSTRAINT uq_linkage_avaliacao_sintetica_avaliacao UNIQUE(avaliacao_id),
    CONSTRAINT ck_linkage_avaliacao_sintetica_seed CHECK(
        gerador_seed>=CONVERT(DECIMAL(20,0),0)
        AND gerador_seed<=CONVERT(DECIMAL(20,0),18446744073709551615)),
    CONSTRAINT ck_linkage_avaliacao_sintetica_finalidade CHECK(
        finalidade=N'ENGINEERING_EVIDENCE_ONLY_NOT_PROMOTABLE'),
    CONSTRAINT ck_linkage_avaliacao_sintetica_natureza CHECK(
        natureza=N'SYNTHETIC_PARAMETER_RECOVERY'),
    CONSTRAINT ck_linkage_avaliacao_sintetica_ambiente CHECK(ambiente_perfil=N'Development'),
    CONSTRAINT ck_linkage_avaliacao_sintetica_status CHECK(status=N'CONCLUIDA'),
    CONSTRAINT ck_linkage_avaliacao_sintetica_contagens CHECK(
        observacoes_materializadas>0 AND observacoes_excluidas>=0),
    CONSTRAINT ck_linkage_avaliacao_sintetica_validacao CHECK(
        validacao_estatistica=N'NOT_ASSESSED_ISSUE_31'),
    CONSTRAINT ck_linkage_avaliacao_sintetica_promocao CHECK(promocao_autorizada=0)
);
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'auditoria.linkage_avaliacao_sintetica')
      AND name=N'IX_linkage_avaliacao_sintetica_modelo')
CREATE INDEX IX_linkage_avaliacao_sintetica_modelo
ON auditoria.linkage_avaliacao_sintetica(
    modelo_id,linkage_avaliacao_sintetica_id DESC)
INCLUDE(
    modelo_versao,grupo_execucao_id,status,gerador_seed,avaliador_versao,
    ambiente_perfil,observacoes_materializadas,observacoes_excluidas,ocorrido_em);
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'auditoria.linkage_avaliacao_sintetica')
      AND name=N'IX_linkage_avaliacao_sintetica_grupo')
CREATE INDEX IX_linkage_avaliacao_sintetica_grupo
ON auditoria.linkage_avaliacao_sintetica(
    grupo_execucao_id,linkage_avaliacao_sintetica_id)
INCLUDE(modelo_id,modelo_versao,gerador_seed,status,ocorrido_em);
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'auditoria.linkage_avaliacao_sintetica')
      AND name=N'UX_linkage_avaliacao_sintetica_report')
CREATE UNIQUE INDEX UX_linkage_avaliacao_sintetica_report
ON auditoria.linkage_avaliacao_sintetica(modelo_id,report_sha256);
GO

IF OBJECT_ID(N'auditoria.linkage_avaliacao_sintetica_metrica',N'U') IS NULL
CREATE TABLE auditoria.linkage_avaliacao_sintetica_metrica(
    linkage_avaliacao_sintetica_metrica_id BIGINT IDENTITY PRIMARY KEY,
    avaliacao_id UNIQUEIDENTIFIER NOT NULL,
    escopo NVARCHAR(80) NOT NULL,
    dimensao NVARCHAR(120) NULL,
    metrica NVARCHAR(120) NOT NULL,
    valor DECIMAL(38,16) NOT NULL,
    unidade NVARCHAR(40) NOT NULL,
    CONSTRAINT fk_linkage_avaliacao_sintetica_metrica_avaliacao
        FOREIGN KEY(avaliacao_id)
        REFERENCES auditoria.linkage_avaliacao_sintetica(avaliacao_id),
    CONSTRAINT ck_linkage_avaliacao_sintetica_metrica_escopo
        CHECK(NULLIF(LTRIM(RTRIM(escopo)),N'') IS NOT NULL),
    CONSTRAINT ck_linkage_avaliacao_sintetica_metrica_nome
        CHECK(NULLIF(LTRIM(RTRIM(metrica)),N'') IS NOT NULL),
    CONSTRAINT ck_linkage_avaliacao_sintetica_metrica_unidade
        CHECK(unidade IN(N'COUNT',N'RATIO',N'PROBABILITY',N'TOTAL_VARIATION'))
);
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'auditoria.linkage_avaliacao_sintetica_metrica')
      AND name=N'UX_linkage_avaliacao_sintetica_metrica_chave')
CREATE UNIQUE INDEX UX_linkage_avaliacao_sintetica_metrica_chave
ON auditoria.linkage_avaliacao_sintetica_metrica(
    avaliacao_id,escopo,dimensao,metrica);
GO

CREATE OR ALTER TRIGGER auditoria.tr_linkage_avaliacao_sintetica_append_only
ON auditoria.linkage_avaliacao_sintetica
INSTEAD OF UPDATE,DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51911,'Evidência sintética de Linkage é append-only.',1;
END;
GO

CREATE OR ALTER TRIGGER auditoria.tr_linkage_avaliacao_sintetica_metrica_append_only
ON auditoria.linkage_avaliacao_sintetica_metrica
INSTEAD OF UPDATE,DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51912,'Métrica de evidência sintética de Linkage é append-only.',1;
END;
GO

CREATE OR ALTER PROCEDURE auditoria.sp_registrar_avaliacao_sintetica_linkage
 @grupo_execucao_id UNIQUEIDENTIFIER,
 @modelo_id UNIQUEIDENTIFIER,
 @modelo_versao INT,
 @modelo_snapshot_sha256 BINARY(32),
 @corpus_fingerprint_sha256 BINARY(32),
 @generation_manifest_sha256 BINARY(32),
 @bridge_manifest_sha256 BINARY(32),
 @observations_sha256 BINARY(32),
 @bridge_truth_sha256 BINARY(32),
 @relatorio_schema_versao NVARCHAR(120),
 @natureza NVARCHAR(80),
 @finalidade NVARCHAR(120),
 @gerador_versao NVARCHAR(120),
 @gerador_seed DECIMAL(20,0),
 @avaliador_versao NVARCHAR(120),
 @ambiente_perfil NVARCHAR(32),
 @status NVARCHAR(20),
 @ruleset_versao NVARCHAR(120),
 @ruleset_fingerprint_sha256 BINARY(32),
 @u_semantica NVARCHAR(120),
 @m_truth_universo NVARCHAR(220),
 @u_truth_universo NVARCHAR(220),
 @u_nome_fonte_nominal NVARCHAR(80),
 @u_nome_mae_fonte_nominal NVARCHAR(80),
 @observacoes_materializadas BIGINT,
 @observacoes_excluidas BIGINT,
 @report_sha256 BINARY(32),
 @metricas auditoria.linkage_avaliacao_sintetica_metrica_tvp READONLY,
 @avaliacao_id UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;

 IF @grupo_execucao_id IS NULL OR @modelo_id IS NULL OR @modelo_versao<=0
    THROW 51913,'Grupo/modelo/versão inválidos para avaliação sintética.',1;
 IF @ambiente_perfil<>N'Development'
    THROW 51914,'Avaliação sintética persistente exige ambiente_perfil=Development.',1;
 IF CONVERT(NVARCHAR(32),(SELECT value FROM sys.extended_properties
                         WHERE class=0 AND name=N'Jornada.EnvironmentProfile'))<>N'Development'
    THROW 51914,'Banco não está marcado como Jornada.EnvironmentProfile=Development.',1;
 IF @status<>N'CONCLUIDA'
    THROW 51915,'Somente avaliação sintética CONCLUIDA pode ser persistida.',1;
 IF @natureza<>N'SYNTHETIC_PARAMETER_RECOVERY'
    OR @finalidade<>N'ENGINEERING_EVIDENCE_ONLY_NOT_PROMOTABLE'
    OR NULLIF(LTRIM(RTRIM(@relatorio_schema_versao)),N'') IS NULL
    THROW 51915,'Natureza/finalidade/schema da avaliação sintética são inválidos.',1;
 IF NULLIF(LTRIM(RTRIM(@m_truth_universo)),N'') IS NULL
    OR NULLIF(LTRIM(RTRIM(@u_truth_universo)),N'') IS NULL
    OR NULLIF(LTRIM(RTRIM(@u_nome_fonte_nominal)),N'') IS NULL
    OR NULLIF(LTRIM(RTRIM(@u_nome_mae_fonte_nominal)),N'') IS NULL
    THROW 51915,'Semântica de m/u sintética incompleta.',1;
 IF @gerador_seed<CONVERT(DECIMAL(20,0),0)
    OR @gerador_seed>CONVERT(DECIMAL(20,0),18446744073709551615)
    THROW 51915,'Seed sintética fora do domínio UInt64.',1;
 IF @observacoes_materializadas<=0 OR @observacoes_excluidas<0
    THROW 51915,'Contagens sintéticas inválidas.',1;
 IF NOT EXISTS(SELECT 1 FROM @metricas)
    THROW 51916,'Avaliação sintética sem métricas agregadas.',1;
 IF EXISTS(
      SELECT escopo,dimensao,metrica
      FROM @metricas
      GROUP BY escopo,dimensao,metrica
      HAVING COUNT(*)>1)
    THROW 51916,'Avaliação sintética contém chave de métrica duplicada.',1;
 IF EXISTS(
      SELECT 1 FROM @metricas
      WHERE NULLIF(LTRIM(RTRIM(escopo)),N'') IS NULL
         OR NULLIF(LTRIM(RTRIM(metrica)),N'') IS NULL
         OR unidade NOT IN(N'COUNT',N'RATIO',N'PROBABILITY',N'TOTAL_VARIATION'))
    THROW 51916,'Avaliação sintética contém métrica agregada inválida.',1;

 DECLARE @status_modelo NVARCHAR(20);
 SELECT @status_modelo=status
 FROM identidade.modelo_linkage WITH(HOLDLOCK)
 WHERE modelo_id=@modelo_id AND versao=@modelo_versao;

 IF @status_modelo IS NULL
    THROW 51917,'Modelo/versão não encontrados para avaliação sintética.',1;
 IF @status_modelo<>N'RASCUNHO'
    THROW 51917,'Avaliação sintética persistente aceita somente modelo RASCUNHO.',1;

 DECLARE @snapshot_corrente BINARY(32);
 EXEC auditoria.sp_calcular_fingerprint_modelo_linkage
      @modelo_id=@modelo_id,
      @fingerprint=@snapshot_corrente OUTPUT;
 IF @snapshot_corrente<>@modelo_snapshot_sha256
    THROW 51918,'Fingerprint do modelo mudou entre avaliação e persistência.',1;

 DECLARE @existente UNIQUEIDENTIFIER;
 SELECT @existente=avaliacao_id
 FROM auditoria.linkage_avaliacao_sintetica WITH(HOLDLOCK)
 WHERE modelo_id=@modelo_id AND report_sha256=@report_sha256;

 IF @existente IS NOT NULL
 BEGIN
   IF NOT EXISTS(
      SELECT 1
      FROM auditoria.linkage_avaliacao_sintetica e
      WHERE e.avaliacao_id=@existente
        AND e.grupo_execucao_id=@grupo_execucao_id
        AND e.modelo_versao=@modelo_versao
        AND e.modelo_snapshot_sha256=@modelo_snapshot_sha256
        AND e.corpus_fingerprint_sha256=@corpus_fingerprint_sha256
        AND e.generation_manifest_sha256=@generation_manifest_sha256
        AND e.bridge_manifest_sha256=@bridge_manifest_sha256
        AND e.observations_sha256=@observations_sha256
        AND e.bridge_truth_sha256=@bridge_truth_sha256
        AND e.relatorio_schema_versao=@relatorio_schema_versao
        AND e.natureza=@natureza
        AND e.finalidade=@finalidade
        AND e.gerador_versao=@gerador_versao
        AND e.gerador_seed=@gerador_seed
        AND e.avaliador_versao=@avaliador_versao
        AND e.ambiente_perfil=@ambiente_perfil
        AND e.status=@status
        AND e.ruleset_versao=@ruleset_versao
        AND e.ruleset_fingerprint_sha256=@ruleset_fingerprint_sha256
        AND e.u_semantica=@u_semantica
        AND e.m_truth_universo=@m_truth_universo
        AND e.u_truth_universo=@u_truth_universo
        AND e.u_nome_fonte_nominal=@u_nome_fonte_nominal
        AND e.u_nome_mae_fonte_nominal=@u_nome_mae_fonte_nominal
        AND e.observacoes_materializadas=@observacoes_materializadas
        AND e.observacoes_excluidas=@observacoes_excluidas)
      THROW 51919,'Hash de relatório sintético já registrado com proveniência incompatível.',1;

   SET @avaliacao_id=@existente;
   RETURN;
 END;

 BEGIN TRANSACTION;
 BEGIN TRY
   SET @avaliacao_id=NEWID();

   INSERT auditoria.linkage_avaliacao_sintetica(
      avaliacao_id,grupo_execucao_id,modelo_id,modelo_versao,modelo_snapshot_sha256,
      corpus_fingerprint_sha256,generation_manifest_sha256,bridge_manifest_sha256,
      observations_sha256,bridge_truth_sha256,relatorio_schema_versao,natureza,finalidade,
      gerador_versao,gerador_seed,avaliador_versao,ambiente_perfil,status,
      ruleset_versao,ruleset_fingerprint_sha256,u_semantica,m_truth_universo,u_truth_universo,
      u_nome_fonte_nominal,u_nome_mae_fonte_nominal,
      observacoes_materializadas,observacoes_excluidas,report_sha256,
      validacao_estatistica,promocao_autorizada,
      executor_aplicacao,executor_login,executor_host,source_revision)
   VALUES(
      @avaliacao_id,@grupo_execucao_id,@modelo_id,@modelo_versao,@modelo_snapshot_sha256,
      @corpus_fingerprint_sha256,@generation_manifest_sha256,@bridge_manifest_sha256,
      @observations_sha256,@bridge_truth_sha256,@relatorio_schema_versao,@natureza,@finalidade,
      @gerador_versao,@gerador_seed,@avaliador_versao,@ambiente_perfil,@status,
      @ruleset_versao,@ruleset_fingerprint_sha256,@u_semantica,@m_truth_universo,@u_truth_universo,
      @u_nome_fonte_nominal,@u_nome_mae_fonte_nominal,
      @observacoes_materializadas,@observacoes_excluidas,@report_sha256,
      N'NOT_ASSESSED_ISSUE_31',0,
      LEFT(COALESCE(APP_NAME(),N'SQL'),128),
      LEFT(COALESCE(ORIGINAL_LOGIN(),SUSER_SNAME(),N'UNKNOWN'),256),
      LEFT(HOST_NAME(),128),
      LEFT(CONVERT(NVARCHAR(80),SESSION_CONTEXT(N'Jornada.SourceRevision')),80));

   INSERT auditoria.linkage_avaliacao_sintetica_metrica(
      avaliacao_id,escopo,dimensao,metrica,valor,unidade)
   SELECT @avaliacao_id,escopo,dimensao,metrica,valor,unidade
   FROM @metricas;

   COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
   IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
   THROW;
 END CATCH;
END;
GO

CREATE OR ALTER VIEW auditoria.v_linkage_avaliacao_sintetica AS
SELECT
    e.linkage_avaliacao_sintetica_id,e.avaliacao_id,e.grupo_execucao_id,
    e.modelo_id,e.modelo_versao,e.modelo_snapshot_sha256,
    e.corpus_fingerprint_sha256,e.generation_manifest_sha256,e.bridge_manifest_sha256,
    e.observations_sha256,e.bridge_truth_sha256,e.relatorio_schema_versao,e.natureza,e.finalidade,
    e.gerador_versao,e.gerador_seed,e.avaliador_versao,e.ambiente_perfil,e.status,e.ruleset_versao,
    e.ruleset_fingerprint_sha256,e.u_semantica,e.m_truth_universo,e.u_truth_universo,
    e.u_nome_fonte_nominal,e.u_nome_mae_fonte_nominal,e.observacoes_materializadas,
    e.observacoes_excluidas,e.report_sha256,e.validacao_estatistica,
    e.promocao_autorizada,e.executor_aplicacao,e.executor_login,e.executor_host,
    e.source_revision,e.ocorrido_em
FROM auditoria.linkage_avaliacao_sintetica e;
GO

CREATE OR ALTER VIEW auditoria.v_linkage_avaliacao_sintetica_metrica AS
SELECT
    m.linkage_avaliacao_sintetica_metrica_id,m.avaliacao_id,
    m.escopo,m.dimensao,m.metrica,m.valor,m.unidade
FROM auditoria.linkage_avaliacao_sintetica_metrica m;
GO
