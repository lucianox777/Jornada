-- Jornada SQL Server: ruleset imutável de blocking associado ao modelo de linkage.
-- Alterações de regra devem produzir nova versão/modelo; não há mutação silenciosa de ruleset existente.

IF OBJECT_ID('identidade.linkage_ruleset','U') IS NULL
BEGIN
    CREATE TABLE identidade.linkage_ruleset(
        ruleset_id UNIQUEIDENTIFIER NOT NULL,
        modelo_id UNIQUEIDENTIFIER NOT NULL,
        ruleset_versao NVARCHAR(120) NOT NULL,
        algoritmo_versao NVARCHAR(80) NOT NULL,
        fingerprint_sha256 CHAR(64) NOT NULL,
        ibge_source_versao NVARCHAR(200) NULL,
        ibge_fingerprint_sha256 CHAR(64) NULL,
        criado_em DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_linkage_ruleset_criado_em DEFAULT(SYSDATETIMEOFFSET()),
        CONSTRAINT PK_linkage_ruleset PRIMARY KEY(ruleset_id),
        CONSTRAINT UQ_linkage_ruleset_modelo UNIQUE(modelo_id),
        CONSTRAINT FK_linkage_ruleset_modelo FOREIGN KEY(modelo_id) REFERENCES identidade.modelo_linkage(modelo_id),
        CONSTRAINT CK_linkage_ruleset_versao CHECK(LEN(LTRIM(RTRIM(ruleset_versao)))>0),
        CONSTRAINT CK_linkage_ruleset_algoritmo CHECK(LEN(LTRIM(RTRIM(algoritmo_versao)))>0),
        CONSTRAINT CK_linkage_ruleset_fingerprint CHECK(LEN(fingerprint_sha256)=64),
        CONSTRAINT CK_linkage_ruleset_ibge_par CHECK(
            (ibge_source_versao IS NULL AND ibge_fingerprint_sha256 IS NULL)
            OR (ibge_source_versao IS NOT NULL AND ibge_fingerprint_sha256 IS NOT NULL AND LEN(ibge_fingerprint_sha256)=64))
    );
END;
GO

IF OBJECT_ID('identidade.linkage_ruleset_passe','U') IS NULL
BEGIN
    CREATE TABLE identidade.linkage_ruleset_passe(
        ruleset_id UNIQUEIDENTIFIER NOT NULL,
        passe_ordem INT NOT NULL,
        passe_id NVARCHAR(120) NOT NULL,
        CONSTRAINT PK_linkage_ruleset_passe PRIMARY KEY(ruleset_id,passe_ordem),
        CONSTRAINT UQ_linkage_ruleset_passe_id UNIQUE(ruleset_id,passe_id),
        CONSTRAINT FK_linkage_ruleset_passe_ruleset FOREIGN KEY(ruleset_id) REFERENCES identidade.linkage_ruleset(ruleset_id),
        CONSTRAINT CK_linkage_ruleset_passe_ordem CHECK(passe_ordem>=0),
        CONSTRAINT CK_linkage_ruleset_passe_id CHECK(LEN(LTRIM(RTRIM(passe_id)))>0)
    );
END;
GO

IF OBJECT_ID('identidade.linkage_ruleset_passe_campo','U') IS NULL
BEGIN
    CREATE TABLE identidade.linkage_ruleset_passe_campo(
        ruleset_id UNIQUEIDENTIFIER NOT NULL,
        passe_ordem INT NOT NULL,
        campo_ordem INT NOT NULL,
        atributo NVARCHAR(80) NOT NULL,
        CONSTRAINT PK_linkage_ruleset_passe_campo PRIMARY KEY(ruleset_id,passe_ordem,campo_ordem),
        CONSTRAINT UQ_linkage_ruleset_passe_campo_atributo UNIQUE(ruleset_id,passe_ordem,atributo),
        CONSTRAINT FK_linkage_ruleset_passe_campo_passe FOREIGN KEY(ruleset_id,passe_ordem)
            REFERENCES identidade.linkage_ruleset_passe(ruleset_id,passe_ordem),
        CONSTRAINT CK_linkage_ruleset_passe_campo_ordem CHECK(campo_ordem>=0),
        CONSTRAINT CK_linkage_ruleset_passe_campo_atributo CHECK(LEN(LTRIM(RTRIM(atributo)))>0)
    );
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_linkage_ruleset_insert_status
ON identidade.linkage_ruleset
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(
        SELECT 1
        FROM inserted i
        JOIN identidade.modelo_linkage m ON m.modelo_id=i.modelo_id
        WHERE m.status NOT IN('GERANDO','RASCUNHO'))
        THROW 51110,'Ruleset só pode ser anexado a modelo em GERANDO/RASCUNHO.',1;
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_linkage_ruleset_immutable
ON identidade.linkage_ruleset
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    THROW 51111,'Ruleset de linkage é imutável; gere nova versão/modelo.',1;
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_linkage_ruleset_passe_guard
ON identidade.linkage_ruleset_passe
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(
        SELECT 1
        FROM inserted i
        JOIN identidade.linkage_ruleset r ON r.ruleset_id=i.ruleset_id
        JOIN identidade.modelo_linkage m ON m.modelo_id=r.modelo_id
        WHERE m.status NOT IN('GERANDO','RASCUNHO'))
        THROW 51112,'Passes só podem ser anexados a ruleset de modelo editável.',1;
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_linkage_ruleset_passe_immutable
ON identidade.linkage_ruleset_passe
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    THROW 51113,'Passe de ruleset é imutável; gere nova versão/modelo.',1;
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_linkage_ruleset_campo_guard
ON identidade.linkage_ruleset_passe_campo
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(
        SELECT 1
        FROM inserted i
        JOIN identidade.linkage_ruleset r ON r.ruleset_id=i.ruleset_id
        JOIN identidade.modelo_linkage m ON m.modelo_id=r.modelo_id
        WHERE m.status NOT IN('GERANDO','RASCUNHO'))
        THROW 51114,'Campos só podem ser anexados a ruleset de modelo editável.',1;
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_linkage_ruleset_campo_immutable
ON identidade.linkage_ruleset_passe_campo
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    THROW 51115,'Campo de passe é imutável; gere nova versão/modelo.',1;
END;
GO
