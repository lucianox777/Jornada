SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('ref.frequencia_nome_versao','U') IS NULL
BEGIN
    CREATE TABLE ref.frequencia_nome_versao(
        frequencia_nome_versao_id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_frequencia_nome_versao PRIMARY KEY,
        codigo NVARCHAR(80) NOT NULL,
        fonte NVARCHAR(200) NOT NULL,
        edicao NVARCHAR(120) NOT NULL,
        data_referencia DATE NOT NULL,
        publicado_em DATE NULL,
        status NVARCHAR(20) NOT NULL CONSTRAINT df_frequencia_nome_versao_status DEFAULT('CARREGANDO'),
        conteudo_sha256 BINARY(32) NULL,
        criado_em DATETIMEOFFSET(7) NOT NULL CONSTRAINT df_frequencia_nome_versao_criado_em DEFAULT(SYSDATETIMEOFFSET()),
        ativado_em DATETIMEOFFSET(7) NULL,
        CONSTRAINT uq_frequencia_nome_versao_codigo UNIQUE(codigo),
        CONSTRAINT ck_frequencia_nome_versao_status CHECK(status IN('CARREGANDO','VALIDADA','ATIVA','OBSOLETA')),
        CONSTRAINT ck_frequencia_nome_versao_hash CHECK(status='CARREGANDO' OR conteudo_sha256 IS NOT NULL),
        CONSTRAINT ck_frequencia_nome_versao_ativacao CHECK((status='ATIVA' AND ativado_em IS NOT NULL) OR status<>'ATIVA')
    );
END;
GO
IF OBJECT_ID('ref.frequencia_nome','U') IS NULL
BEGIN
    CREATE TABLE ref.frequencia_nome(
        frequencia_nome_id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_frequencia_nome PRIMARY KEY,
        frequencia_nome_versao_id BIGINT NOT NULL,
        tipo NVARCHAR(20) NOT NULL,
        valor NVARCHAR(200) NOT NULL,
        valor_normalizado NVARCHAR(200) NOT NULL,
        sexo NVARCHAR(20) NOT NULL CONSTRAINT df_frequencia_nome_sexo DEFAULT('TODOS'),
        periodo_nascimento NVARCHAR(40) NOT NULL CONSTRAINT df_frequencia_nome_periodo DEFAULT('TODOS'),
        escopo_geografico NVARCHAR(20) NOT NULL,
        uf_codigo CHAR(2) NOT NULL CONSTRAINT df_frequencia_nome_uf DEFAULT('00'),
        municipio_codigo CHAR(7) NOT NULL CONSTRAINT df_frequencia_nome_municipio DEFAULT('0000000'),
        frequencia BIGINT NOT NULL,
        criado_em DATETIMEOFFSET(7) NOT NULL CONSTRAINT df_frequencia_nome_criado_em DEFAULT(SYSDATETIMEOFFSET()),
        CONSTRAINT fk_frequencia_nome_versao FOREIGN KEY(frequencia_nome_versao_id) REFERENCES ref.frequencia_nome_versao(frequencia_nome_versao_id),
        CONSTRAINT ck_frequencia_nome_tipo CHECK(tipo IN('NOME','SOBRENOME')),
        CONSTRAINT ck_frequencia_nome_sexo CHECK(sexo IN('TODOS','MASCULINO','FEMININO')),
        CONSTRAINT ck_frequencia_nome_escopo CHECK(escopo_geografico IN('BRASIL','UF','MUNICIPIO')),
        CONSTRAINT ck_frequencia_nome_geo CHECK((escopo_geografico='BRASIL' AND uf_codigo='00' AND municipio_codigo='0000000') OR (escopo_geografico='UF' AND uf_codigo<>'00' AND municipio_codigo='0000000') OR (escopo_geografico='MUNICIPIO' AND uf_codigo<>'00' AND municipio_codigo<>'0000000')),
        CONSTRAINT ck_frequencia_nome_frequencia CHECK(frequencia>0),
        CONSTRAINT uq_frequencia_nome_dimensao UNIQUE(frequencia_nome_versao_id,tipo,valor,sexo,periodo_nascimento,escopo_geografico,uf_codigo,municipio_codigo)
    );
END;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('ref.frequencia_nome') AND name='ix_frequencia_nome_lookup_normalizado')
    CREATE INDEX ix_frequencia_nome_lookup_normalizado ON ref.frequencia_nome(frequencia_nome_versao_id,tipo,valor_normalizado,escopo_geografico,uf_codigo,municipio_codigo,sexo,periodo_nascimento) INCLUDE(frequencia);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('ref.frequencia_nome_versao') AND name='ux_frequencia_nome_versao_ativa')
    CREATE UNIQUE INDEX ux_frequencia_nome_versao_ativa ON ref.frequencia_nome_versao(status) WHERE status='ATIVA';
GO
CREATE OR ALTER TRIGGER ref.tr_frequencia_nome_bloqueia_versao_publicada ON ref.frequencia_nome AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM (SELECT frequencia_nome_versao_id FROM inserted UNION SELECT frequencia_nome_versao_id FROM deleted) x JOIN ref.frequencia_nome_versao v ON v.frequencia_nome_versao_id=x.frequencia_nome_versao_id WHERE v.status<>'CARREGANDO')
  THROW 51630,'Frequências de uma versão publicada não podem ser alteradas.',1;
END;
GO
CREATE OR ALTER TRIGGER ref.tr_frequencia_nome_versao_metadado_immutavel ON ref.frequencia_nome_versao AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.frequencia_nome_versao_id=d.frequencia_nome_versao_id WHERE i.frequencia_nome_versao_id IS NULL AND d.status<>'CARREGANDO')
  THROW 51631,'Versão publicada de frequências não pode ser excluída.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.frequencia_nome_versao_id=i.frequencia_nome_versao_id WHERE d.status<>'CARREGANDO' AND (i.codigo<>d.codigo OR i.fonte<>d.fonte OR i.edicao<>d.edicao OR i.data_referencia<>d.data_referencia OR ISNULL(i.publicado_em,'19000101')<>ISNULL(d.publicado_em,'19000101') OR ISNULL(i.conteudo_sha256,0x00)<>ISNULL(d.conteudo_sha256,0x00)))
  THROW 51632,'Metadados de uma versão publicada de frequências são imutáveis.',1;
END;
GO
CREATE OR ALTER PROCEDURE ref.sp_publicar_frequencia_nome_versao @frequencia_nome_versao_id BIGINT,@conteudo_sha256 BINARY(32) AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @conteudo_sha256 IS NULL THROW 51633,'Publicação exige SHA-256 do conteúdo carregado.',1;
 DECLARE @started BIT=0;
 IF @@TRANCOUNT=0 BEGIN BEGIN TRANSACTION; SET @started=1; END;
 BEGIN TRY
  DECLARE @status NVARCHAR(20);
  SELECT @status=status FROM ref.frequencia_nome_versao WITH(UPDLOCK,HOLDLOCK) WHERE frequencia_nome_versao_id=@frequencia_nome_versao_id;
  IF @status IS NULL THROW 51634,'Versão de frequências inexistente.',1;
  IF @status<>'CARREGANDO' THROW 51635,'Somente versão CARREGANDO pode ser publicada.',1;
  IF NOT EXISTS(SELECT 1 FROM ref.frequencia_nome WHERE frequencia_nome_versao_id=@frequencia_nome_versao_id) THROW 51636,'Versão sem frequências não pode ser publicada.',1;
  IF NOT EXISTS(SELECT 1 FROM ref.frequencia_nome WHERE frequencia_nome_versao_id=@frequencia_nome_versao_id AND tipo='NOME') THROW 51637,'Versão deve conter ao menos uma frequência de NOME.',1;
  IF NOT EXISTS(SELECT 1 FROM ref.frequencia_nome WHERE frequencia_nome_versao_id=@frequencia_nome_versao_id AND tipo='SOBRENOME') THROW 51638,'Versão deve conter ao menos uma frequência de SOBRENOME.',1;
  UPDATE ref.frequencia_nome_versao SET status='OBSOLETA' WHERE status='ATIVA' AND frequencia_nome_versao_id<>@frequencia_nome_versao_id;
  UPDATE ref.frequencia_nome_versao SET conteudo_sha256=@conteudo_sha256,status='ATIVA',ativado_em=SYSDATETIMEOFFSET() WHERE frequencia_nome_versao_id=@frequencia_nome_versao_id;
  IF @started=1 COMMIT TRANSACTION;
 END TRY BEGIN CATCH IF @started=1 AND XACT_STATE()<>0 ROLLBACK TRANSACTION; THROW; END CATCH
END;
GO
CREATE OR ALTER VIEW ref.v_frequencia_nome_ativa AS
SELECT f.frequencia_nome_id,f.frequencia_nome_versao_id,v.codigo AS versao_codigo,v.data_referencia,v.conteudo_sha256,f.tipo,f.valor,f.valor_normalizado,f.sexo,f.periodo_nascimento,f.escopo_geografico,f.uf_codigo,f.municipio_codigo,f.frequencia
FROM ref.frequencia_nome f JOIN ref.frequencia_nome_versao v ON v.frequencia_nome_versao_id=f.frequencia_nome_versao_id WHERE v.status='ATIVA';
GO
IF COL_LENGTH('identidade.modelo_linkage','frequencia_nome_versao_id') IS NULL ALTER TABLE identidade.modelo_linkage ADD frequencia_nome_versao_id BIGINT NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('identidade.modelo_linkage') AND name='fk_modelo_linkage_frequencia_nome_versao')
 ALTER TABLE identidade.modelo_linkage WITH CHECK ADD CONSTRAINT fk_modelo_linkage_frequencia_nome_versao FOREIGN KEY(frequencia_nome_versao_id) REFERENCES ref.frequencia_nome_versao(frequencia_nome_versao_id);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('identidade.modelo_linkage') AND name='ix_modelo_linkage_frequencia_nome_versao')
 CREATE INDEX ix_modelo_linkage_frequencia_nome_versao ON identidade.modelo_linkage(frequencia_nome_versao_id) WHERE frequencia_nome_versao_id IS NOT NULL;
GO

-- Integração fail-closed com o Calibrador: qualquer novo modelo GERANDO captura
-- atomicamente a versão ATIVA vigente. O Worker não precisa consultar a referência
-- separadamente, evitando TOCTOU entre seleção da versão e criação do modelo.
CREATE OR ALTER TRIGGER identidade.tr_modelo_linkage_fixa_frequencia_nome_versao
ON identidade.modelo_linkage
AFTER INSERT,UPDATE
AS
BEGIN
 SET NOCOUNT ON;
 IF TRIGGER_NESTLEVEL()>1 RETURN;

 IF EXISTS(
   SELECT 1 FROM inserted i
   LEFT JOIN deleted d ON d.modelo_id=i.modelo_id
   WHERE d.modelo_id IS NULL AND i.status='GERANDO' AND i.frequencia_nome_versao_id IS NULL)
 BEGIN
   DECLARE @ativa BIGINT=(SELECT frequencia_nome_versao_id FROM ref.frequencia_nome_versao WITH(HOLDLOCK) WHERE status='ATIVA');
   IF @ativa IS NULL THROW 51639,'Geração de modelo exige uma versão ATIVA da referência de frequências.',1;
   UPDATE m SET frequencia_nome_versao_id=@ativa
   FROM identidade.modelo_linkage m JOIN inserted i ON i.modelo_id=m.modelo_id
   LEFT JOIN deleted d ON d.modelo_id=i.modelo_id
   WHERE d.modelo_id IS NULL AND i.status='GERANDO' AND i.frequencia_nome_versao_id IS NULL;
 END;

 IF EXISTS(
   SELECT 1 FROM inserted i JOIN deleted d ON d.modelo_id=i.modelo_id
   WHERE ISNULL(i.frequencia_nome_versao_id,-1)<>ISNULL(d.frequencia_nome_versao_id,-1))
   THROW 51640,'A versão da referência de frequências de um modelo é imutável após sua criação.',1;
END;
GO
