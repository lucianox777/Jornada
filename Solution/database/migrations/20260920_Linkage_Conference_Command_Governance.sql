SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Governança do comando de conferência independente
  -------------------------------------------------
  Follow-up imutável da migration de evidência já publicada em master.
  Torna o registro:
  - idempotente por relatório + request + snapshot + revisão;
  - vinculado obrigatoriamente à revisão técnica que executou a conferência;
  - fail-closed quando o mesmo hash é reutilizado com conteúdo incompatível.
*/

IF OBJECT_ID(N'auditoria.linkage_conferencia_evidencia',N'U') IS NULL
   OR OBJECT_ID(N'auditoria.sp_calcular_fingerprint_modelo_linkage',N'P') IS NULL
    THROW 51993,'Governança do comando de conferência exige o contrato de evidência instalado.',1;
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
 IF @validacao_estatistica<>N'NOT_ASSESSED_ISSUE_31'
    THROW 51990,'Conferência de implementação não pode declarar validação estatística da issue #31.',1;
 IF DATALENGTH(@request_sha256)<>32 OR DATALENGTH(@report_sha256)<>32
    THROW 51977,'Hashes SHA-256 da conferência são obrigatórios.',1;

 DECLARE @source_revision NVARCHAR(80)=
   LEFT(CONVERT(NVARCHAR(80),SESSION_CONTEXT(N'Jornada.SourceRevision')),80);
 IF NULLIF(LTRIM(RTRIM(@source_revision)),N'') IS NULL
    THROW 51992,'Registro de conferência exige source_revision técnica explícita.',1;

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
         OR (@mesma_decisao_final<>0
             AND (@max_llr_par_observado IS NULL
                  OR @max_llr_par_observado<=@max_llr_par_permitido)))
    THROW 51981,'Evidência DIVERGENTE não demonstra divergência primária.',1;

 IF @status=N'NAO_EXECUTADA' AND @motivo IS NULL
    THROW 51982,'Evidência NAO_EXECUTADA exige motivo.',1;

 DECLARE @modelo_snapshot_sha256 BINARY(32);
 EXEC auditoria.sp_calcular_fingerprint_modelo_linkage
      @modelo_id=@modelo_id,
      @fingerprint=@modelo_snapshot_sha256 OUTPUT;

 DECLARE @existente UNIQUEIDENTIFIER=NULL,
         @existente_request_sha256 BINARY(32)=NULL,
         @existente_modelo_snapshot_sha256 BINARY(32)=NULL,
         @existente_source_revision NVARCHAR(80)=NULL,
         @existente_modelo_versao INT=NULL,
         @existente_metodo NVARCHAR(120)=NULL,
         @existente_tolerancia NVARCHAR(120)=NULL,
         @existente_status NVARCHAR(20)=NULL;

 SELECT
   @existente=evidencia_id,
   @existente_request_sha256=request_sha256,
   @existente_modelo_snapshot_sha256=modelo_snapshot_sha256,
   @existente_source_revision=source_revision,
   @existente_modelo_versao=modelo_versao,
   @existente_metodo=metodo_versao,
   @existente_tolerancia=tolerancia_versao,
   @existente_status=status
 FROM auditoria.linkage_conferencia_evidencia WITH(UPDLOCK,HOLDLOCK)
 WHERE modelo_id=@modelo_id
   AND report_sha256=@report_sha256;

 IF @existente IS NOT NULL
 BEGIN
   IF @existente_request_sha256<>@request_sha256
      OR @existente_modelo_snapshot_sha256<>@modelo_snapshot_sha256
      OR @existente_source_revision<>@source_revision
      OR @existente_modelo_versao<>@modelo_versao
      OR @existente_metodo<>@metodo_versao
      OR @existente_tolerancia<>@tolerancia_versao
      OR @existente_status<>@status
      THROW 51991,'Hash de relatório já registrado com conteúdo/snapshot/revisão incompatível.',1;

   SET @evidencia_id=@existente;
   RETURN;
 END;

 SET @evidencia_id=NEWID();

 INSERT auditoria.linkage_conferencia_evidencia(
   evidencia_id,modelo_id,modelo_versao,metodo_versao,escopo,
   tolerancia_versao,max_llr_par_permitido,status,candidatos_avaliados,
   max_llr_par_observado,max_log_odds_observado,mesma_decisao_final,mesmo_top1,
   spearman,motivo,validacao_estatistica,modelo_snapshot_sha256,request_sha256,report_sha256,
   executor_aplicacao,executor_login,executor_host,source_revision)
 VALUES(
   @evidencia_id,@modelo_id,@modelo_versao,@metodo_versao,@escopo,
   @tolerancia_versao,@max_llr_par_permitido,@status,@candidatos_avaliados,
   @max_llr_par_observado,@max_log_odds_observado,@mesma_decisao_final,@mesmo_top1,
   @spearman,@motivo,@validacao_estatistica,@modelo_snapshot_sha256,@request_sha256,@report_sha256,
   LEFT(COALESCE(APP_NAME(),N'SQL'),128),
   LEFT(COALESCE(ORIGINAL_LOGIN(),SUSER_SNAME(),N'UNKNOWN'),256),
   LEFT(HOST_NAME(),128),
   @source_revision);
END;
GO
