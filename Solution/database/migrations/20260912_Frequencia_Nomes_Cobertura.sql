SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('ref.frequencia_nome_cobertura','U') IS NULL
BEGIN
    CREATE TABLE ref.frequencia_nome_cobertura(
        frequencia_nome_cobertura_id BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT pk_frequencia_nome_cobertura PRIMARY KEY,
        frequencia_nome_versao_id BIGINT NOT NULL,
        tipo NVARCHAR(20) NOT NULL,
        escopo_geografico NVARCHAR(20) NOT NULL,
        inclui_sexo BIT NOT NULL CONSTRAINT df_frequencia_nome_cobertura_sexo DEFAULT(0),
        inclui_periodo_nascimento BIT NOT NULL CONSTRAINT df_frequencia_nome_cobertura_periodo DEFAULT(0),
        cobertura NVARCHAR(20) NOT NULL,
        ausencia_semantica NVARCHAR(40) NOT NULL
            CONSTRAINT df_frequencia_nome_cobertura_ausencia DEFAULT('NAO_PUBLICADA_OU_SUPRIMIDA'),
        origem_endpoint NVARCHAR(400) NOT NULL,
        observacao NVARCHAR(500) NULL,
        criado_em DATETIMEOFFSET(7) NOT NULL
            CONSTRAINT df_frequencia_nome_cobertura_criado_em DEFAULT(SYSDATETIMEOFFSET()),
        CONSTRAINT fk_frequencia_nome_cobertura_versao
            FOREIGN KEY(frequencia_nome_versao_id)
            REFERENCES ref.frequencia_nome_versao(frequencia_nome_versao_id),
        CONSTRAINT ck_frequencia_nome_cobertura_tipo CHECK(tipo IN('NOME','SOBRENOME')),
        CONSTRAINT ck_frequencia_nome_cobertura_escopo CHECK(escopo_geografico IN('BRASIL','UF','MUNICIPIO')),
        CONSTRAINT ck_frequencia_nome_cobertura_estado CHECK(cobertura IN('COMPLETA','PARCIAL')),
        CONSTRAINT ck_frequencia_nome_cobertura_ausencia CHECK(
            ausencia_semantica IN('NAO_PUBLICADA_OU_SUPRIMIDA','NAO_APLICAVEL')),
        CONSTRAINT uq_frequencia_nome_cobertura UNIQUE(
            frequencia_nome_versao_id,tipo,escopo_geografico,inclui_sexo,inclui_periodo_nascimento)
    );
END;
GO

CREATE OR ALTER TRIGGER ref.tr_frequencia_nome_cobertura_bloqueia_versao_publicada
ON ref.frequencia_nome_cobertura
AFTER INSERT,UPDATE,DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS(
        SELECT 1
        FROM (
            SELECT frequencia_nome_versao_id FROM inserted
            UNION
            SELECT frequencia_nome_versao_id FROM deleted
        ) x
        JOIN ref.frequencia_nome_versao v
          ON v.frequencia_nome_versao_id=x.frequencia_nome_versao_id
        WHERE v.status<>'CARREGANDO')
        THROW 51650,'Cobertura de uma versão publicada de frequências não pode ser alterada.',1;
END;
GO

CREATE OR ALTER VIEW ref.v_frequencia_nome_cobertura_ativa
AS
SELECT
    c.frequencia_nome_cobertura_id,
    c.frequencia_nome_versao_id,
    v.codigo AS versao_codigo,
    c.tipo,
    c.escopo_geografico,
    c.inclui_sexo,
    c.inclui_periodo_nascimento,
    c.cobertura,
    c.ausencia_semantica,
    c.origem_endpoint,
    c.observacao
FROM ref.frequencia_nome_cobertura c
JOIN ref.frequencia_nome_versao v
  ON v.frequencia_nome_versao_id=c.frequencia_nome_versao_id
WHERE v.status='ATIVA';
GO
