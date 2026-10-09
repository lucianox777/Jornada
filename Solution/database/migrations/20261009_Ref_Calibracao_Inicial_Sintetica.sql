SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

-- Snapshot versionado dos resultados do bootstrap do Calibrador sobre corpus sintético.
-- Os dados estimados não são observações IBGE: origem explicitamente SINTETICA.
-- Payload contém resultados FS, thresholds, TF e dependências condicionais em JSON.
IF OBJECT_ID(N'ref.calibracao_inicial_versao',N'U') IS NULL
BEGIN
 CREATE TABLE ref.calibracao_inicial_versao(
  calibracao_versao_id BIGINT IDENTITY(1,1) NOT NULL
    CONSTRAINT pk_ref_calibracao_inicial_versao PRIMARY KEY,
  codigo NVARCHAR(120) NOT NULL
    CONSTRAINT uq_ref_calibracao_inicial_codigo UNIQUE,
  modelo_id UNIQUEIDENTIFIER NULL,
  origem NVARCHAR(30) NOT NULL
    CONSTRAINT df_ref_calibracao_inicial_origem DEFAULT(N'SINTETICA'),
  corpus_codigo NVARCHAR(160) NOT NULL,
  corpus_sha256 CHAR(64) NOT NULL,
  referencia_ibge_codigo NVARCHAR(120) NOT NULL,
  algoritmo_versao NVARCHAR(120) NOT NULL,
  metodo NVARCHAR(160) NOT NULL,
  estado NVARCHAR(20) NOT NULL
    CONSTRAINT df_ref_calibracao_inicial_estado DEFAULT(N'CARREGANDO'),
  resultados_json NVARCHAR(MAX) NOT NULL,
  resultados_sha256 CHAR(64) NOT NULL,
  criado_em DATETIMEOFFSET(7) NOT NULL
    CONSTRAINT df_ref_calibracao_inicial_criado DEFAULT(SYSDATETIMEOFFSET()),
  publicado_em DATETIMEOFFSET(7) NULL,
  CONSTRAINT fk_ref_calibracao_inicial_modelo FOREIGN KEY(modelo_id)
    REFERENCES identidade.modelo_linkage(modelo_id),
  CONSTRAINT ck_ref_calibracao_inicial_origem CHECK(origem=N'SINTETICA'),
  CONSTRAINT ck_ref_calibracao_inicial_estado CHECK(estado IN(N'CARREGANDO',N'PUBLICADA')),
  CONSTRAINT ck_ref_calibracao_inicial_json CHECK(ISJSON(resultados_json)=1),
  CONSTRAINT ck_ref_calibracao_inicial_sha CHECK(
    LEN(corpus_sha256)=64 AND corpus_sha256 NOT LIKE '%[^0-9A-F]%'
    AND LEN(resultados_sha256)=64 AND resultados_sha256 NOT LIKE '%[^0-9A-F]%'),
  CONSTRAINT ck_ref_calibracao_inicial_publicacao CHECK(
    (estado=N'CARREGANDO' AND publicado_em IS NULL)
    OR (estado=N'PUBLICADA' AND publicado_em IS NOT NULL))
 );
END;
GO

CREATE OR ALTER TRIGGER ref.tr_calibracao_inicial_publicada_immutable
ON ref.calibracao_inicial_versao
AFTER UPDATE,DELETE
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted WHERE estado=N'PUBLICADA')
   THROW 52220,'Snapshot publicado da calibração inicial é imutável.',1;
END;
GO

CREATE OR ALTER PROCEDURE ref.sp_publicar_calibracao_inicial
 @calibracao_versao_id BIGINT
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 BEGIN TRANSACTION;
 BEGIN TRY
   DECLARE @estado NVARCHAR(20),@json NVARCHAR(MAX),@sha CHAR(64);
   SELECT @estado=estado,@json=resultados_json,@sha=resultados_sha256
   FROM ref.calibracao_inicial_versao WITH(UPDLOCK,HOLDLOCK)
   WHERE calibracao_versao_id=@calibracao_versao_id;
   IF @estado IS NULL THROW 52221,'Versão de calibração inicial inexistente.',1;
   IF @estado<>N'CARREGANDO' THROW 52222,'Apenas versão CARREGANDO pode ser publicada.',1;
   IF UPPER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONVERT(VARBINARY(MAX),@json)),2))<>@sha
     THROW 52223,'Hash dos resultados da calibração inicial não confere.',1;
   UPDATE ref.calibracao_inicial_versao
     SET estado=N'PUBLICADA',publicado_em=SYSDATETIMEOFFSET()
     WHERE calibracao_versao_id=@calibracao_versao_id;
   COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
   IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
   THROW;
 END CATCH
END;
GO


-- Idempotência por código: a carga deve reutilizar a versão já publicada
-- somente quando hashes de corpus/resultados forem idênticos.
CREATE OR ALTER PROCEDURE ref.sp_registrar_calibracao_inicial
 @codigo NVARCHAR(120),
 @corpus_codigo NVARCHAR(160),
 @corpus_sha256 CHAR(64),
 @referencia_ibge_codigo NVARCHAR(120),
 @algoritmo_versao NVARCHAR(120),
 @metodo NVARCHAR(160),
 @resultados_json NVARCHAR(MAX),
 @modelo_id UNIQUEIDENTIFIER=NULL,
 @calibracao_versao_id BIGINT OUTPUT
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 IF ISJSON(@resultados_json)<>1
   THROW 52224,'Resultados da calibração inicial devem ser JSON válido.',1;
 DECLARE @sha CHAR(64)=UPPER(CONVERT(VARCHAR(64),
   HASHBYTES('SHA2_256',CONVERT(VARBINARY(MAX),@resultados_json)),2));
 BEGIN TRANSACTION;
 BEGIN TRY
   SELECT @calibracao_versao_id=calibracao_versao_id
   FROM ref.calibracao_inicial_versao WITH(UPDLOCK,HOLDLOCK)
   WHERE codigo=@codigo;
   IF @calibracao_versao_id IS NOT NULL
   BEGIN
     IF NOT EXISTS(
       SELECT 1 FROM ref.calibracao_inicial_versao
       WHERE calibracao_versao_id=@calibracao_versao_id
         AND corpus_codigo=@corpus_codigo AND corpus_sha256=@corpus_sha256
         AND referencia_ibge_codigo=@referencia_ibge_codigo
         AND algoritmo_versao=@algoritmo_versao AND metodo=@metodo
         AND resultados_sha256=@sha
         AND ((modelo_id=@modelo_id) OR (modelo_id IS NULL AND @modelo_id IS NULL)))
       THROW 52225,'Código de calibração inicial já existe com conteúdo ou origem diferente.',1;
   END
   ELSE
   BEGIN
     INSERT ref.calibracao_inicial_versao(
       codigo,modelo_id,corpus_codigo,corpus_sha256,
       referencia_ibge_codigo,algoritmo_versao,metodo,resultados_json,resultados_sha256)
     VALUES(@codigo,@modelo_id,@corpus_codigo,@corpus_sha256,
       @referencia_ibge_codigo,@algoritmo_versao,@metodo,@resultados_json,@sha);
     SET @calibracao_versao_id=CONVERT(BIGINT,SCOPE_IDENTITY());
   END;
   COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
   IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
   THROW;
 END CATCH
END;
GO
