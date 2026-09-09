-- Serialização entre writers determinísticos e composição governada.
-- O mesmo lock lógico de referência é adquirido pelo leitor/aplicador de composição.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO
IF OBJECT_ID('identidade.sp_publicar_referencia_progressiva_deterministica','P') IS NULL
 THROW 51510,'Persistência progressiva determinística não instalada.',1;
GO
CREATE OR ALTER PROCEDURE identidade.sp_publicar_referencia_progressiva_deterministica
 @pessoa_origem_id BIGINT,
 @canonical_uuid UNIQUEIDENTIFIER,
 @evidencia_referencia NVARCHAR(255),
 @politica_versao NVARCHAR(120),
 @versao_resultado BIGINT OUTPUT
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 IF @@TRANCOUNT=0 THROW 51118,'Publicação de referência exige transação explícita.',1;
 IF @pessoa_origem_id IS NULL OR @pessoa_origem_id<=0 THROW 51115,'Origem inválida.',1;
 IF @canonical_uuid IS NULL OR @canonical_uuid='00000000-0000-0000-0000-000000000000' THROW 51119,'UUID canônico inválido.',1;
 IF @evidencia_referencia IS NULL OR LTRIM(RTRIM(@evidencia_referencia))='' OR @politica_versao IS NULL OR LTRIM(RTRIM(@politica_versao))=''
   THROW 51120,'Publicação de referência exige evidência e política.',1;

 DECLARE @reference_lock INT;
 DECLARE @reference_resource NVARCHAR(255)=N'JORNADA:COMPOSICAO:REF:'+LOWER(CONVERT(NVARCHAR(36),@canonical_uuid));
 EXEC @reference_lock=sys.sp_getapplock
   @Resource=@reference_resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @reference_lock<0 THROW 51511,'Não foi possível serializar a referência com a composição governada.',1;

 DECLARE @ensure TABLE(initial_uuid UNIQUEIDENTIFIER NOT NULL,legacy_pessoa_uuid UNIQUEIDENTIFIER NULL,estado VARCHAR(20) NOT NULL,versao BIGINT NOT NULL);
 INSERT INTO @ensure(initial_uuid,legacy_pessoa_uuid,estado,versao)
 EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@pessoa_origem_id;
 DECLARE @initial UNIQUEIDENTIFIER,@current UNIQUEIDENTIFIER,@estado VARCHAR(20),@versao BIGINT,@nova BIGINT,@now DATETIMEOFFSET(7),@externo UNIQUEIDENTIFIER;
 SELECT @initial=initial_uuid,@current=canonical_uuid,@estado=estado,@versao=versao
   FROM identidade.pessoa_origem_progressiva WITH(UPDLOCK,HOLDLOCK)
  WHERE pessoa_origem_id=@pessoa_origem_id;
 IF @estado='REFERENCIA' AND @current=@canonical_uuid
 BEGIN SET @versao_resultado=@versao; RETURN; END;
 IF @estado='REFERENCIA' AND (@current<>@canonical_uuid OR @current IS NULL)
   THROW 51121,'Referência progressiva já aponta outro UUID; correção governada necessária.',1;
 IF NOT EXISTS(SELECT 1 FROM identidade.pessoa WITH(UPDLOCK,HOLDLOCK) WHERE pessoa_uuid=@canonical_uuid)
   THROW 51122,'UUID canônico inexistente.',1;
 SET @nova=@versao+1;
 SET @now=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00');
 SET @externo=CASE WHEN @canonical_uuid=@initial THEN NULL ELSE @canonical_uuid END;
 INSERT identidade.pessoa_origem_progressiva_evento(
  evento_id,pessoa_origem_id,versao,tipo,estado,canonical_uuid,expected_version,resultado,target_uuid,
  evidencia_referencia,politica_versao,modelo_versao,universo_referencia,completo,ocorrido_em)
 VALUES(
  NEWID(),@pessoa_origem_id,@nova,'RESOLUCAO','REFERENCIA',@canonical_uuid,@versao,'ASSOCIACAO_EXISTENTE',@canonical_uuid,
  @evidencia_referencia,@politica_versao,NULL,NULL,1,@now);
 UPDATE identidade.pessoa_origem_progressiva
    SET canonical_uuid=@canonical_uuid,estado='REFERENCIA',versao=@nova,
        ultima_resolucao_em=@now,ultimo_destino_externo_uuid=@externo,atualizado_em=@now
  WHERE pessoa_origem_id=@pessoa_origem_id;
 SET @versao_resultado=@nova;
END;
GO
