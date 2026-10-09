SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

-- Distribuição demográfica operacional versionada na camada ref.
-- Fonte PROJETADA: população viva em 2026; não são nascimentos observados.
IF OBJECT_ID(N'ref.distribuicao_nascimento_versao',N'U') IS NULL
BEGIN
 CREATE TABLE ref.distribuicao_nascimento_versao(
   distribuicao_versao_id BIGINT IDENTITY(1,1) NOT NULL
     CONSTRAINT pk_ref_distribuicao_nascimento_versao PRIMARY KEY,
   codigo NVARCHAR(120) NOT NULL
     CONSTRAINT uq_ref_distribuicao_nascimento_versao_codigo UNIQUE,
   fonte NVARCHAR(160) NOT NULL,
   geografia NVARCHAR(40) NOT NULL,
   data_referencia DATE NOT NULL,
   metodo NVARCHAR(160) NOT NULL,
   fonte_arquivo_sha256 CHAR(64) NOT NULL,
   status NVARCHAR(20) NOT NULL
     CONSTRAINT df_ref_distribuicao_nascimento_status DEFAULT(N'CARREGANDO'),
   publicado_em DATETIMEOFFSET(7) NULL,
   CONSTRAINT ck_ref_distribuicao_nascimento_status
     CHECK(status IN(N'CARREGANDO',N'PUBLICADA')),
   CONSTRAINT ck_ref_distribuicao_nascimento_sha
     CHECK(LEN(fonte_arquivo_sha256)=64
       AND fonte_arquivo_sha256 NOT LIKE '%[^0-9A-F]%'),
   CONSTRAINT ck_ref_distribuicao_nascimento_publicado
     CHECK((status=N'CARREGANDO' AND publicado_em IS NULL)
       OR (status=N'PUBLICADA' AND publicado_em IS NOT NULL))
 );
END;
GO

IF OBJECT_ID(N'ref.distribuicao_nascimento_dia',N'U') IS NULL
BEGIN
 CREATE TABLE ref.distribuicao_nascimento_dia(
   distribuicao_versao_id BIGINT NOT NULL,
   data_nascimento DATE NOT NULL,
   peso_populacional BIGINT NOT NULL,
   CONSTRAINT pk_ref_distribuicao_nascimento_dia
     PRIMARY KEY(distribuicao_versao_id,data_nascimento),
   CONSTRAINT fk_ref_distribuicao_nascimento_dia_versao
     FOREIGN KEY(distribuicao_versao_id)
     REFERENCES ref.distribuicao_nascimento_versao(distribuicao_versao_id),
   CONSTRAINT ck_ref_distribuicao_nascimento_peso CHECK(peso_populacional>=0)
 );
END;
GO

CREATE OR ALTER TRIGGER ref.tr_distribuicao_nascimento_dia_immutable
ON ref.distribuicao_nascimento_dia AFTER INSERT,UPDATE,DELETE
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(
   SELECT 1 FROM (
     SELECT distribuicao_versao_id FROM inserted
     UNION SELECT distribuicao_versao_id FROM deleted
   ) x JOIN ref.distribuicao_nascimento_versao v
     ON v.distribuicao_versao_id=x.distribuicao_versao_id
   WHERE v.status=N'PUBLICADA')
   THROW 52230,'Distribuição demográfica publicada é imutável.',1;
END;
GO

CREATE OR ALTER TRIGGER ref.tr_distribuicao_nascimento_versao_immutable
ON ref.distribuicao_nascimento_versao AFTER UPDATE,DELETE
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted WHERE status=N'PUBLICADA')
   THROW 52231,'Versão demográfica publicada é imutável.',1;
END;
GO

CREATE OR ALTER PROCEDURE ref.sp_publicar_distribuicao_nascimento
 @distribuicao_versao_id BIGINT,
 @linhas_esperadas BIGINT,
 @peso_total_esperado BIGINT
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 BEGIN TRANSACTION;
 BEGIN TRY
   DECLARE @status NVARCHAR(20),@linhas BIGINT,@peso BIGINT;
   SELECT @status=status FROM ref.distribuicao_nascimento_versao WITH(UPDLOCK,HOLDLOCK)
   WHERE distribuicao_versao_id=@distribuicao_versao_id;
   IF @status IS NULL THROW 52232,'Versão demográfica inexistente.',1;
   IF @status<>N'CARREGANDO' THROW 52233,'Apenas versão CARREGANDO pode ser publicada.',1;
   SELECT @linhas=COUNT_BIG(*),@peso=COALESCE(SUM(peso_populacional),0)
   FROM ref.distribuicao_nascimento_dia WITH(HOLDLOCK)
   WHERE distribuicao_versao_id=@distribuicao_versao_id;
   IF @linhas_esperadas<=0 OR @peso_total_esperado<=0
     OR @linhas<>@linhas_esperadas OR @peso<>@peso_total_esperado
     THROW 52234,'Contagem ou peso demográfico não confere com manifesto.',1;
   UPDATE ref.distribuicao_nascimento_versao
     SET status=N'PUBLICADA',publicado_em=SYSDATETIMEOFFSET()
   WHERE distribuicao_versao_id=@distribuicao_versao_id;
   COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
   IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
   THROW;
 END CATCH
END;
GO
