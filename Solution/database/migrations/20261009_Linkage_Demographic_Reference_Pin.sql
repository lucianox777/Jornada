SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

-- Pin de proveniência demográfica por modelo. Não duplica os 39.268 pesos diários
-- do artefato de referência; registra apenas a identidade verificável do arquivo.
IF OBJECT_ID(N'identidade.modelo_linkage_referencia_demografica',N'U') IS NULL
BEGIN
 CREATE TABLE identidade.modelo_linkage_referencia_demografica(
   modelo_id UNIQUEIDENTIFIER NOT NULL
     CONSTRAINT pk_modelo_linkage_referencia_demografica PRIMARY KEY,
   codigo_referencia NVARCHAR(120) NOT NULL,
   geografia NVARCHAR(40) NOT NULL,
   data_referencia DATE NOT NULL,
   arquivo_sha256 CHAR(64) NOT NULL,
   metodo NVARCHAR(120) NOT NULL,
   registrado_em DATETIMEOFFSET(7) NOT NULL
     CONSTRAINT df_modelo_linkage_ref_demografica_registrado DEFAULT(SYSDATETIMEOFFSET()),
   CONSTRAINT fk_modelo_linkage_ref_demografica_modelo
     FOREIGN KEY(modelo_id) REFERENCES identidade.modelo_linkage(modelo_id),
   CONSTRAINT ck_modelo_linkage_ref_demografica_hash
     CHECK(LEN(arquivo_sha256)=64 AND arquivo_sha256 NOT LIKE '%[^0-9A-F]%'),
   CONSTRAINT ck_modelo_linkage_ref_demografica_campos
     CHECK(LEN(LTRIM(RTRIM(codigo_referencia)))>0
       AND LEN(LTRIM(RTRIM(geografia)))>0
       AND LEN(LTRIM(RTRIM(metodo)))>0)
 );
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_modelo_linkage_ref_demografica_guard
ON identidade.modelo_linkage_referencia_demografica
AFTER INSERT
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(
   SELECT 1 FROM inserted i
   JOIN identidade.modelo_linkage m ON m.modelo_id=i.modelo_id
   WHERE m.status NOT IN(N'GERANDO',N'RASCUNHO'))
  THROW 52210,'Referência demográfica só pode ser fixada durante geração/rascunho.',1;
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_modelo_linkage_ref_demografica_immutable
ON identidade.modelo_linkage_referencia_demografica
INSTEAD OF UPDATE,DELETE
AS
BEGIN
 THROW 52211,'Pin demográfico imutável; crie nova versão de modelo.',1;
END;
GO
