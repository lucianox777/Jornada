SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH(N'auditoria.modelo_linkage_aprovacao',N'dossie_sha256') IS NULL
BEGIN
    ALTER TABLE auditoria.modelo_linkage_aprovacao
        ADD dossie_sha256 BINARY(32) NULL;
END;
GO

CREATE OR ALTER PROCEDURE auditoria.sp_registrar_aprovacao_modelo_linkage
 @modelo_id UNIQUEIDENTIFIER,
 @acao NVARCHAR(20),
 @dossie_sha256 BINARY(32),
 @decisor NVARCHAR(256),
 @motivo NVARCHAR(1000),
 @aprovacao_id UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @acao NOT IN(N'VALIDATE',N'ACTIVATE') THROW 51871,'Ação de aprovação inválida.',1;
 IF @dossie_sha256 IS NULL OR DATALENGTH(@dossie_sha256)<>32
   THROW 51894,'SHA-256 do dossiê aprovado é obrigatório.',1;
 IF NULLIF(LTRIM(RTRIM(@decisor)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(@motivo)),N'') IS NULL
   THROW 51872,'Decisor e motivo são obrigatórios.',1;

 DECLARE @status NVARCHAR(20),@snapshot BINARY(32),@ativo UNIQUEIDENTIFIER;
 SELECT @status=status FROM identidade.modelo_linkage WITH(HOLDLOCK) WHERE modelo_id=@modelo_id;
 IF @status IS NULL THROW 51873,'Modelo não encontrado.',1;
 IF (@acao=N'VALIDATE' AND @status<>N'RASCUNHO') OR (@acao=N'ACTIVATE' AND @status<>N'VALIDADO')
   THROW 51874,'Estado do modelo incompatível com a aprovação.',1;

 EXEC auditoria.sp_assert_dossie_decisao_modelo_linkage
      @modelo_id=@modelo_id,@dossie_sha256=@dossie_sha256;

 SELECT @ativo=modelo_id FROM identidade.modelo_linkage WITH(HOLDLOCK) WHERE status=N'ATIVO';
 EXEC auditoria.sp_calcular_fingerprint_modelo_linkage @modelo_id=@modelo_id,@fingerprint=@snapshot OUTPUT;
 SET @aprovacao_id=NEWID();
 INSERT auditoria.modelo_linkage_aprovacao
 (aprovacao_id,modelo_id,acao,modelo_snapshot_sha256,ativo_base_modelo_id,dossie_sha256,decisor,motivo)
 VALUES
 (@aprovacao_id,@modelo_id,@acao,@snapshot,@ativo,@dossie_sha256,LEFT(@decisor,256),LEFT(@motivo,1000));
END;
GO

CREATE OR ALTER PROCEDURE auditoria.sp_assert_aprovacao_modelo_linkage
 @modelo_id UNIQUEIDENTIFIER,@acao NVARCHAR(20)
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @id BIGINT,@snapshot BINARY(32),@current BINARY(32),@ativo_base UNIQUEIDENTIFIER,@ativo UNIQUEIDENTIFIER,@dossie_sha256 BINARY(32);
 SELECT TOP(1)
   @id=modelo_linkage_aprovacao_id,
   @snapshot=modelo_snapshot_sha256,
   @ativo_base=ativo_base_modelo_id,
   @dossie_sha256=dossie_sha256
 FROM auditoria.modelo_linkage_aprovacao WITH(HOLDLOCK)
 WHERE modelo_id=@modelo_id AND acao=@acao
 ORDER BY modelo_linkage_aprovacao_id DESC;
 IF @id IS NULL THROW 51875,'Promoção bloqueada: aprovação humana ausente.',1;
 IF @dossie_sha256 IS NULL THROW 51895,'Promoção bloqueada: aprovação humana não está vinculada a dossiê decisório.',1;

 EXEC auditoria.sp_assert_dossie_decisao_modelo_linkage
      @modelo_id=@modelo_id,@dossie_sha256=@dossie_sha256;

 EXEC auditoria.sp_calcular_fingerprint_modelo_linkage @modelo_id=@modelo_id,@fingerprint=@current OUTPUT;
 IF @current<>@snapshot THROW 51876,'Promoção bloqueada: modelo mudou após aprovação humana.',1;
 SELECT @ativo=modelo_id FROM identidade.modelo_linkage WITH(HOLDLOCK) WHERE status=N'ATIVO';
 IF ISNULL(CONVERT(NVARCHAR(36),@ativo),N'')<>ISNULL(CONVERT(NVARCHAR(36),@ativo_base),N'')
   THROW 51877,'Promoção bloqueada: modelo ATIVO mudou após aprovação humana.',1;
END;
GO
