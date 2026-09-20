SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Evidência agregada da conferência independente do Linkage
  ---------------------------------------------------------
  Persiste somente metadados agregados por modelo. Não persiste nomes, CPF,
  datas de nascimento, candidate_id, vetores por par ou scores par-a-par.

  Este contrato NÃO ativa o gate de promoção por si só. A procedure
  auditoria.sp_assert_conferencia_linkage_conforme fica disponível para o
  futuro wiring em VALIDATE/ACTIVATE quando a tolerância governada deixar de
  estar UNFROZEN.
*/

IF OBJECT_ID(N'identidade.modelo_linkage',N'U') IS NULL
    THROW 51970,'Evidência de conferência exige identidade.modelo_linkage instalado.',1;
GO

IF OBJECT_ID(N'auditoria.linkage_conferencia_evidencia',N'U') IS NULL
CREATE TABLE auditoria.linkage_conferencia_evidencia(
    linkage_conferencia_evidencia_id BIGINT IDENTITY PRIMARY KEY,
    evidencia_id UNIQUEIDENTIFIER NOT NULL,
    modelo_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.modelo_linkage(modelo_id),
    modelo_versao INT NOT NULL,
    metodo_versao NVARCHAR(120) NOT NULL,
    escopo NVARCHAR(220) NOT NULL,
    tolerancia_versao NVARCHAR(120) NOT NULL,
    max_llr_par_permitido DECIMAL(28,16) NOT NULL,
    status NVARCHAR(20) NOT NULL,
    candidatos_avaliados INT NOT NULL,
    max_llr_par_observado DECIMAL(28,16) NULL,
    max_log_odds_observado DECIMAL(28,16) NULL,
    mesma_decisao_final BIT NOT NULL,
    mesmo_top1 BIT NOT NULL,
    spearman DECIMAL(18,12) NULL,
    motivo NVARCHAR(120) NULL,
    validacao_estatistica NVARCHAR(80) NOT NULL,
    request_sha256 BINARY(32) NOT NULL,
    report_sha256 BINARY(32) NOT NULL,
    executor_aplicacao NVARCHAR(128) NOT NULL,
    executor_login NVARCHAR(256) NOT NULL,
    executor_host NVARCHAR(128) NULL,
    source_revision NVARCHAR(80) NULL,
    ocorrido_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
    CONSTRAINT uq_linkage_conferencia_evidencia_id UNIQUE(evidencia_id),
    CONSTRAINT ck_linkage_conferencia_status CHECK(status IN(N'CONFORME',N'DIVERGENTE',N'NAO_EXECUTADA')),
    CONSTRAINT ck_linkage_conferencia_contagens CHECK(candidatos_avaliados>=0),
    CONSTRAINT ck_linkage_conferencia_tolerancia CHECK(max_llr_par_permitido>=0),
    CONSTRAINT ck_linkage_conferencia_spearman CHECK(spearman IS NULL OR (spearman>=-1 AND spearman<=1)),
    CONSTRAINT ck_linkage_conferencia_resultado CHECK(
      (status=N'CONFORME'
       AND candidatos_avaliados>0
       AND max_llr_par_observado IS NOT NULL
       AND max_llr_par_observado<=max_llr_par_permitido
       AND mesma_decisao_final=1
       AND motivo IS NULL)
      OR
      (status=N'DIVERGENTE'
       AND candidatos_avaliados>0
       AND motivo IS NOT NULL
       AND (mesma_decisao_final=0
            OR (max_llr_par_observado IS NOT NULL
                AND max_llr_par_observado>max_llr_par_permitido)))
      OR
      (status=N'NAO_EXECUTADA'
       AND motivo IS NOT NULL))
);
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'auditoria.linkage_conferencia_evidencia')
      AND name=N'IX_linkage_conferencia_evidencia_modelo')
CREATE INDEX IX_linkage_conferencia_evidencia_modelo
ON auditoria.linkage_conferencia_evidencia(
    modelo_id,metodo_versao,tolerancia_versao,linkage_conferencia_evidencia_id DESC)
INCLUDE(modelo_versao,status,max_llr_par_permitido,max_llr_par_observado,
        mesma_decisao_final,mesmo_top1,ocorrido_em);
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'auditoria.linkage_conferencia_evidencia')
      AND name=N'UX_linkage_conferencia_evidencia_report')
CREATE UNIQUE INDEX UX_linkage_conferencia_evidencia_report
ON auditoria.linkage_conferencia_evidencia(modelo_id,report_sha256);
GO

CREATE OR ALTER TRIGGER auditoria.tr_linkage_conferencia_evidencia_append_only
ON auditoria.linkage_conferencia_evidencia
INSTEAD OF UPDATE,DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51971,'Evidência de conferência de Linkage é append-only.',1;
END;
GO

CREATE OR ALTER PROCEDURE auditoria.sp_registrar_conferencia_linkage
 @modelo_id UNIQUEIDENTIFIER,
 @modelo_versao INT,
 @metodo_versao NVARCHAR(120),
 @escopo NVARCHAR(220),
 @tolerancia_versao NVARCHAR(120),
 @max_llr_par_permitido DECIMAL(28,16),
 @status NVARCHAR(20),
 @candidatos_avaliados INT,
 @max_llr_par_observado DECIMAL(28,16)=NULL,
 @max_log_odds_observado DECIMAL(28,16)=NULL,
 @mesma_decisao_final BIT,
 @mesmo_top1 BIT,
 @spearman DECIMAL(18,12)=NULL,
 @motivo NVARCHAR(120)=NULL,
 @validacao_estatistica NVARCHAR(80),
 @request_sha256 BINARY(32),
 @report_sha256 BINARY(32),
 @evidencia_id UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;

 IF @modelo_id IS NULL OR @modelo_versao<=0
    THROW 51972,'Modelo inválido para evidência de conferência.',1;
 IF NULLIF(LTRIM(RTRIM(@metodo_versao)),N'') IS NULL
    OR NULLIF(LTRIM(RTRIM(@escopo)),N'') IS NULL
    OR NULLIF(LTRIM(RTRIM(@tolerancia_versao)),N'') IS NULL
    THROW 51973,'Método, escopo e versão de tolerância são obrigatórios.',1;
 IF @max_llr_par_permitido<0
    THROW 51974,'Tolerância de LLR não pode ser negativa.',1;
 IF @status NOT IN(N'CONFORME',N'DIVERGENTE',N'NAO_EXECUTADA')
    THROW 51975,'Status de conferência inválido.',1;
 IF @candidatos_avaliados<0
    THROW 51976,'Contagem de candidatos inválida.',1;
 IF DATALENGTH(@request_sha256)<>32 OR DATALENGTH(@report_sha256)<>32
    THROW 51977,'Hashes SHA-256 da conferência são obrigatórios.',1;

 DECLARE @status_modelo NVARCHAR(20);
 SELECT @status_modelo=status
 FROM identidade.modelo_linkage WITH(HOLDLOCK)
 WHERE modelo_id=@modelo_id AND versao=@modelo_versao;

 IF @status_modelo IS NULL
    THROW 51978,'Modelo/versão não encontrados para a conferência.',1;
 IF @status_modelo<>N'RASCUNHO'
    THROW 51979,'Conferência governada só pode ser registrada para modelo RASCUNHO.',1;

 IF @status=N'CONFORME'
    AND (@candidatos_avaliados<=0 OR @max_llr_par_observado IS NULL
         OR @max_llr_par_observado>@max_llr_par_permitido
         OR @mesma_decisao_final<>1 OR @motivo IS NOT NULL)
    THROW 51980,'Evidência CONFORME não satisfaz os gates primários.',1;

 IF @status=N'DIVERGENTE'
    AND (@candidatos_avaliados<=0 OR @motivo IS NULL
         OR NOT(@mesma_decisao_final=0 OR @max_llr_par_observado>@max_llr_par_permitido))
    THROW 51981,'Evidência DIVERGENTE não demonstra divergência primária.',1;

 IF @status=N'NAO_EXECUTADA' AND @motivo IS NULL
    THROW 51982,'Evidência NAO_EXECUTADA exige motivo.',1;

 SET @evidencia_id=NEWID();

 INSERT auditoria.linkage_conferencia_evidencia(
   evidencia_id,modelo_id,modelo_versao,metodo_versao,escopo,
   tolerancia_versao,max_llr_par_permitido,status,candidatos_avaliados,
   max_llr_par_observado,max_log_odds_observado,mesma_decisao_final,mesmo_top1,
   spearman,motivo,validacao_estatistica,request_sha256,report_sha256,
   executor_aplicacao,executor_login,executor_host,source_revision)
 VALUES(
   @evidencia_id,@modelo_id,@modelo_versao,@metodo_versao,@escopo,
   @tolerancia_versao,@max_llr_par_permitido,@status,@candidatos_avaliados,
   @max_llr_par_observado,@max_log_odds_observado,@mesma_decisao_final,@mesmo_top1,
   @spearman,@motivo,@validacao_estatistica,@request_sha256,@report_sha256,
   LEFT(COALESCE(APP_NAME(),N'SQL'),128),
   LEFT(COALESCE(ORIGINAL_LOGIN(),SUSER_SNAME(),N'UNKNOWN'),256),
   LEFT(HOST_NAME(),128),
   LEFT(CONVERT(NVARCHAR(80),SESSION_CONTEXT(N'Jornada.SourceRevision')),80));
END;
GO

CREATE OR ALTER PROCEDURE auditoria.sp_assert_conferencia_linkage_conforme
 @modelo_id UNIQUEIDENTIFIER,
 @metodo_versao NVARCHAR(120),
 @tolerancia_versao NVARCHAR(120),
 @max_llr_par_permitido DECIMAL(28,16)
AS
BEGIN
 SET NOCOUNT ON;

 DECLARE @evidencia_id BIGINT,@status NVARCHAR(20),@tol DECIMAL(28,16),
         @same BIT,@max_obs DECIMAL(28,16),@modelo_versao INT;

 SELECT TOP(1)
   @evidencia_id=linkage_conferencia_evidencia_id,
   @status=status,
   @tol=max_llr_par_permitido,
   @same=mesma_decisao_final,
   @max_obs=max_llr_par_observado,
   @modelo_versao=modelo_versao
 FROM auditoria.linkage_conferencia_evidencia WITH(HOLDLOCK)
 WHERE modelo_id=@modelo_id
   AND metodo_versao=@metodo_versao
   AND tolerancia_versao=@tolerancia_versao
 ORDER BY linkage_conferencia_evidencia_id DESC;

 IF @evidencia_id IS NULL
    THROW 51983,'Modelo sem evidência de conferência para método/tolerância exigidos.',1;
 IF @status<>N'CONFORME'
    THROW 51984,'Última evidência de conferência não está CONFORME.',1;
 IF @tol<>@max_llr_par_permitido
    THROW 51985,'Tolerância da evidência diverge da tolerância governada exigida.',1;
 IF @same<>1 OR @max_obs IS NULL OR @max_obs>@max_llr_par_permitido
    THROW 51986,'Evidência CONFORME perdeu consistência com os gates primários.',1;
 IF NOT EXISTS(
      SELECT 1 FROM identidade.modelo_linkage
      WHERE modelo_id=@modelo_id AND versao=@modelo_versao
        AND status IN(N'RASCUNHO',N'VALIDADO'))
    THROW 51987,'Evidência não corresponde ao estado promocional corrente do modelo.',1;
END;
GO

CREATE OR ALTER VIEW auditoria.v_linkage_conferencia_evidencia AS
SELECT
  e.linkage_conferencia_evidencia_id,e.evidencia_id,
  e.modelo_id,e.modelo_versao,e.metodo_versao,e.escopo,
  e.tolerancia_versao,e.max_llr_par_permitido,e.status,e.candidatos_avaliados,
  e.max_llr_par_observado,e.max_log_odds_observado,e.mesma_decisao_final,
  e.mesmo_top1,e.spearman,e.motivo,e.validacao_estatistica,
  e.request_sha256,e.report_sha256,
  e.executor_aplicacao,e.executor_login,e.executor_host,e.source_revision,e.ocorrido_em
FROM auditoria.linkage_conferencia_evidencia e;
GO
