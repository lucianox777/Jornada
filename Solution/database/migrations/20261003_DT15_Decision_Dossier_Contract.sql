SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'auditoria.modelo_linkage_dossie_decisao',N'U') IS NULL
CREATE TABLE auditoria.modelo_linkage_dossie_decisao(
    modelo_linkage_dossie_decisao_id BIGINT IDENTITY PRIMARY KEY,
    dossie_id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    modelo_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.modelo_linkage(modelo_id),
    ativo_base_modelo_id UNIQUEIDENTIFIER NULL REFERENCES identidade.modelo_linkage(modelo_id),
    modelo_snapshot_sha256 BINARY(32) NOT NULL,
    dossie_sha256 BINARY(32) NOT NULL,
    contrato_versao NVARCHAR(80) NOT NULL,
    estado NVARCHAR(20) NOT NULL,
    origem_evidencia NVARCHAR(40) NOT NULL,
    valido_ate DATETIMEOFFSET(7) NOT NULL,
    referencia_artefato NVARCHAR(1000) NULL,
    registrado_por NVARCHAR(256) NOT NULL,
    ocorrido_em DATETIMEOFFSET(7) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT uq_modelo_linkage_dossie_decisao_id UNIQUE(dossie_id),
    CONSTRAINT uq_modelo_linkage_dossie_decisao_hash UNIQUE(modelo_id,dossie_sha256),
    CONSTRAINT ck_modelo_linkage_dossie_decisao_estado CHECK(estado IN(N'COMPLETO',N'INCOMPLETO',N'NAO_COMPARAVEL')),
    CONSTRAINT ck_modelo_linkage_dossie_decisao_origem CHECK(origem_evidencia IN(N'SINTETICA_DEV',N'REPRESENTATIVA_HML',N'REPRESENTATIVA_PROD')),
    CONSTRAINT ck_modelo_linkage_dossie_decisao_registrado_por CHECK(LEN(LTRIM(RTRIM(registrado_por)))>0)
);
GO

CREATE OR ALTER TRIGGER auditoria.tr_modelo_linkage_dossie_decisao_append_only
ON auditoria.modelo_linkage_dossie_decisao
INSTEAD OF UPDATE,DELETE
AS
BEGIN
 SET NOCOUNT ON;
 THROW 51880,'Dossiê decisório de modelo é append-only.',1;
END;
GO

CREATE OR ALTER PROCEDURE auditoria.sp_registrar_dossie_decisao_modelo_linkage
 @modelo_id UNIQUEIDENTIFIER,
 @dossie_sha256 BINARY(32),
 @contrato_versao NVARCHAR(80),
 @estado NVARCHAR(20),
 @origem_evidencia NVARCHAR(40),
 @valido_ate DATETIMEOFFSET(7),
 @referencia_artefato NVARCHAR(1000)=NULL,
 @registrado_por NVARCHAR(256),
 @dossie_id UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @dossie_sha256 IS NULL OR DATALENGTH(@dossie_sha256)<>32 THROW 51881,'SHA-256 do dossiê é obrigatório.',1;
 IF NULLIF(LTRIM(RTRIM(@contrato_versao)),N'') IS NULL THROW 51882,'Versão do contrato do dossiê é obrigatória.',1;
 IF @estado NOT IN(N'COMPLETO',N'INCOMPLETO',N'NAO_COMPARAVEL') THROW 51883,'Estado do dossiê inválido.',1;
 IF @origem_evidencia NOT IN(N'SINTETICA_DEV',N'REPRESENTATIVA_HML',N'REPRESENTATIVA_PROD') THROW 51884,'Origem de evidência inválida.',1;
 IF @valido_ate<=SYSDATETIMEOFFSET() THROW 51885,'Dossiê deve possuir validade futura.',1;
 IF NULLIF(LTRIM(RTRIM(@registrado_por)),N'') IS NULL THROW 51886,'Registrador do dossiê é obrigatório.',1;

 DECLARE @status NVARCHAR(20),@snapshot BINARY(32),@ativo UNIQUEIDENTIFIER;
 SELECT @status=status FROM identidade.modelo_linkage WITH(HOLDLOCK) WHERE modelo_id=@modelo_id;
 IF @status IS NULL THROW 51887,'Modelo do dossiê não encontrado.',1;
 IF @status NOT IN(N'RASCUNHO',N'VALIDADO') THROW 51888,'Dossiê somente pode ser registrado para RASCUNHO ou VALIDADO.',1;
 SELECT @ativo=modelo_id FROM identidade.modelo_linkage WITH(HOLDLOCK) WHERE status=N'ATIVO';
 EXEC auditoria.sp_calcular_fingerprint_modelo_linkage @modelo_id=@modelo_id,@fingerprint=@snapshot OUTPUT;

 SET @dossie_id=NEWID();
 INSERT auditoria.modelo_linkage_dossie_decisao
 (dossie_id,modelo_id,ativo_base_modelo_id,modelo_snapshot_sha256,dossie_sha256,contrato_versao,estado,origem_evidencia,valido_ate,referencia_artefato,registrado_por)
 VALUES
 (@dossie_id,@modelo_id,@ativo,@snapshot,@dossie_sha256,LEFT(@contrato_versao,80),@estado,@origem_evidencia,@valido_ate,NULLIF(LEFT(LTRIM(RTRIM(@referencia_artefato)),1000),N''),LEFT(@registrado_por,256));
END;
GO

CREATE OR ALTER PROCEDURE auditoria.sp_assert_dossie_decisao_modelo_linkage
 @modelo_id UNIQUEIDENTIFIER,
 @dossie_sha256 BINARY(32)
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @snapshot BINARY(32),@current BINARY(32),@ativo_base UNIQUEIDENTIFIER,@ativo UNIQUEIDENTIFIER,@estado NVARCHAR(20),@valido_ate DATETIMEOFFSET(7);
 SELECT TOP(1) @snapshot=modelo_snapshot_sha256,@ativo_base=ativo_base_modelo_id,@estado=estado,@valido_ate=valido_ate
 FROM auditoria.modelo_linkage_dossie_decisao WITH(HOLDLOCK)
 WHERE modelo_id=@modelo_id AND dossie_sha256=@dossie_sha256
 ORDER BY modelo_linkage_dossie_decisao_id DESC;
 IF @snapshot IS NULL THROW 51889,'Dossiê decisório não registrado.',1;
 IF @estado<>N'COMPLETO' THROW 51890,'Dossiê decisório não está completo.',1;
 IF @valido_ate<=SYSDATETIMEOFFSET() THROW 51891,'Dossiê decisório expirado.',1;
 EXEC auditoria.sp_calcular_fingerprint_modelo_linkage @modelo_id=@modelo_id,@fingerprint=@current OUTPUT;
 IF @current<>@snapshot THROW 51892,'Modelo mudou após geração do dossiê decisório.',1;
 SELECT @ativo=modelo_id FROM identidade.modelo_linkage WITH(HOLDLOCK) WHERE status=N'ATIVO';
 IF ISNULL(CONVERT(NVARCHAR(36),@ativo),N'')<>ISNULL(CONVERT(NVARCHAR(36),@ativo_base),N'')
   THROW 51893,'Modelo ATIVO mudou após geração do dossiê decisório.',1;
END;
GO
