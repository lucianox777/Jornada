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
