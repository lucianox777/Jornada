SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

-- #903, fase 1: somente catalogo. Nao altera Silver/Gold nem ativa precedencia.
-- Codigos estaveis; versoes publicadas imutaveis. Sem seed destrutivo.
IF OBJECT_ID(N'ref.tipo_documento',N'U') IS NULL
BEGIN
 CREATE TABLE ref.tipo_documento (
   tipo_documento_codigo NVARCHAR(40) NOT NULL CONSTRAINT pk_tipo_documento PRIMARY KEY,
   descricao NVARCHAR(160) NOT NULL,
   ativo BIT NOT NULL CONSTRAINT df_tipo_documento_ativo DEFAULT(1)
 );
END;
GO

IF OBJECT_ID(N'ref.modelo_documento',N'U') IS NULL
BEGIN
 CREATE TABLE ref.modelo_documento (
   modelo_documento_id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_modelo_documento PRIMARY KEY,
   tipo_documento_codigo NVARCHAR(40) NOT NULL,
   codigo NVARCHAR(80) NOT NULL,
   versao INT NOT NULL,
   descricao NVARCHAR(200) NOT NULL,
   estado NVARCHAR(20) NOT NULL CONSTRAINT df_modelo_documento_estado DEFAULT(N'RASCUNHO'),
   publicado_em DATETIMEOFFSET(7) NULL,
   CONSTRAINT fk_modelo_documento_tipo FOREIGN KEY(tipo_documento_codigo)
     REFERENCES ref.tipo_documento(tipo_documento_codigo),
   CONSTRAINT uq_modelo_documento_codigo_versao UNIQUE(codigo,versao),
   CONSTRAINT ck_modelo_documento_versao CHECK(versao>0),
   CONSTRAINT ck_modelo_documento_estado CHECK(estado IN(N'RASCUNHO',N'PUBLICADO')),
   CONSTRAINT ck_modelo_documento_publicacao CHECK(
      (estado=N'RASCUNHO' AND publicado_em IS NULL)
      OR (estado=N'PUBLICADO' AND publicado_em IS NOT NULL))
 );
END;
GO

IF OBJECT_ID(N'ref.modelo_documento_atributo',N'U') IS NULL
BEGIN
 CREATE TABLE ref.modelo_documento_atributo (
   modelo_documento_id BIGINT NOT NULL,
   atributo_codigo NVARCHAR(80) NOT NULL,
   comprovacao_admissivel BIT NOT NULL CONSTRAINT df_modelo_documento_atributo_admissivel DEFAULT(1),
   CONSTRAINT pk_modelo_documento_atributo PRIMARY KEY(modelo_documento_id,atributo_codigo),
   CONSTRAINT fk_modelo_documento_atributo_modelo FOREIGN KEY(modelo_documento_id)
     REFERENCES ref.modelo_documento(modelo_documento_id),
   CONSTRAINT ck_modelo_documento_atributo_codigo CHECK(LEN(LTRIM(RTRIM(atributo_codigo)))>0)
 );
END;
GO

-- Bloqueia qualquer mutacao de versao ja publicada, inclusive seus atributos.
CREATE OR ALTER TRIGGER ref.tr_modelo_documento_publicado_imutavel
ON ref.modelo_documento AFTER UPDATE,DELETE
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted WHERE estado=N'PUBLICADO')
   THROW 52301,'Modelo documental publicado e imutavel; crie nova versao.',1;
END;
GO

CREATE OR ALTER TRIGGER ref.tr_modelo_documento_atributo_publicado_imutavel
ON ref.modelo_documento_atributo AFTER INSERT,UPDATE,DELETE
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(
   SELECT 1 FROM (
     SELECT modelo_documento_id FROM inserted
     UNION SELECT modelo_documento_id FROM deleted
   ) x JOIN ref.modelo_documento m ON m.modelo_documento_id=x.modelo_documento_id
   WHERE m.estado=N'PUBLICADO')
   THROW 52302,'Atributos de modelo publicado sao imutaveis.',1;
END;
GO

-- Cadastro inicial nao sobrescreve governanca local nem presume RG deterministico.
IF NOT EXISTS(SELECT 1 FROM ref.tipo_documento WHERE tipo_documento_codigo=N'RG')
 INSERT ref.tipo_documento(tipo_documento_codigo,descricao) VALUES(N'RG',N'Registro Geral');
IF NOT EXISTS(SELECT 1 FROM ref.tipo_documento WHERE tipo_documento_codigo=N'CIN')
 INSERT ref.tipo_documento(tipo_documento_codigo,descricao) VALUES(N'CIN',N'Carteira de Identidade Nacional');
GO
