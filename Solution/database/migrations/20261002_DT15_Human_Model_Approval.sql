SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'auditoria.modelo_linkage_aprovacao',N'U') IS NULL
CREATE TABLE auditoria.modelo_linkage_aprovacao(
    modelo_linkage_aprovacao_id BIGINT IDENTITY PRIMARY KEY,
    aprovacao_id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    modelo_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.modelo_linkage(modelo_id),
    acao NVARCHAR(20) NOT NULL,
    modelo_snapshot_sha256 BINARY(32) NOT NULL,
    ativo_base_modelo_id UNIQUEIDENTIFIER NULL REFERENCES identidade.modelo_linkage(modelo_id),
    decisor NVARCHAR(256) NOT NULL,
    motivo NVARCHAR(1000) NOT NULL,
    ocorrido_em DATETIMEOFFSET(7) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT uq_modelo_linkage_aprovacao_id UNIQUE(aprovacao_id),
    CONSTRAINT ck_modelo_linkage_aprovacao_acao CHECK(acao IN(N'VALIDATE',N'ACTIVATE')),
    CONSTRAINT ck_modelo_linkage_aprovacao_decisor CHECK(LEN(LTRIM(RTRIM(decisor)))>0),
    CONSTRAINT ck_modelo_linkage_aprovacao_motivo CHECK(LEN(LTRIM(RTRIM(motivo)))>0)
);
GO

CREATE OR ALTER TRIGGER auditoria.tr_modelo_linkage_aprovacao_append_only
ON auditoria.modelo_linkage_aprovacao
INSTEAD OF UPDATE,DELETE
AS
BEGIN
 SET NOCOUNT ON;
 THROW 51870,'Aprovação humana de modelo é append-only.',1;
END;
GO

CREATE OR ALTER PROCEDURE auditoria.sp_registrar_aprovacao_modelo_linkage
 @modelo_id UNIQUEIDENTIFIER,
 @acao NVARCHAR(20),
 @decisor NVARCHAR(256),
 @motivo NVARCHAR(1000),
 @aprovacao_id UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @acao NOT IN(N'VALIDATE',N'ACTIVATE') THROW 51871,'Ação de aprovação inválida.',1;
 IF NULLIF(LTRIM(RTRIM(@decisor)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(@motivo)),N'') IS NULL
   THROW 51872,'Decisor e motivo são obrigatórios.',1;

 DECLARE @status NVARCHAR(20),@snapshot BINARY(32),@ativo UNIQUEIDENTIFIER;
 SELECT @status=status FROM identidade.modelo_linkage WITH(HOLDLOCK) WHERE modelo_id=@modelo_id;
 IF @status IS NULL THROW 51873,'Modelo não encontrado.',1;
 IF (@acao=N'VALIDATE' AND @status<>N'RASCUNHO') OR (@acao=N'ACTIVATE' AND @status<>N'VALIDADO')
   THROW 51874,'Estado do modelo incompatível com a aprovação.',1;
 SELECT @ativo=modelo_id FROM identidade.modelo_linkage WITH(HOLDLOCK) WHERE status=N'ATIVO';
 EXEC auditoria.sp_calcular_fingerprint_modelo_linkage @modelo_id=@modelo_id,@fingerprint=@snapshot OUTPUT;
 SET @aprovacao_id=NEWID();
 INSERT auditoria.modelo_linkage_aprovacao(aprovacao_id,modelo_id,acao,modelo_snapshot_sha256,ativo_base_modelo_id,decisor,motivo)
 VALUES(@aprovacao_id,@modelo_id,@acao,@snapshot,@ativo,LEFT(@decisor,256),LEFT(@motivo,1000));
END;
GO

CREATE OR ALTER PROCEDURE auditoria.sp_assert_aprovacao_modelo_linkage
 @modelo_id UNIQUEIDENTIFIER,@acao NVARCHAR(20)
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @id BIGINT,@snapshot BINARY(32),@current BINARY(32),@ativo_base UNIQUEIDENTIFIER,@ativo UNIQUEIDENTIFIER;
 SELECT TOP(1) @id=modelo_linkage_aprovacao_id,@snapshot=modelo_snapshot_sha256,@ativo_base=ativo_base_modelo_id
 FROM auditoria.modelo_linkage_aprovacao WITH(HOLDLOCK)
 WHERE modelo_id=@modelo_id AND acao=@acao ORDER BY modelo_linkage_aprovacao_id DESC;
 IF @id IS NULL THROW 51875,'Promoção bloqueada: aprovação humana ausente.',1;
 EXEC auditoria.sp_calcular_fingerprint_modelo_linkage @modelo_id=@modelo_id,@fingerprint=@current OUTPUT;
 IF @current<>@snapshot THROW 51876,'Promoção bloqueada: modelo mudou após aprovação humana.',1;
 SELECT @ativo=modelo_id FROM identidade.modelo_linkage WITH(HOLDLOCK) WHERE status=N'ATIVO';
 IF ISNULL(CONVERT(NVARCHAR(36),@ativo),N'')<>ISNULL(CONVERT(NVARCHAR(36),@ativo_base),N'')
   THROW 51877,'Promoção bloqueada: modelo ATIVO mudou após aprovação humana.',1;
END;
GO
