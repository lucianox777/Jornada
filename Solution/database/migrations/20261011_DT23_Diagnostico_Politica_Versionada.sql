-- DT-23: armazenamento versionado, estritamente observacional.
-- Esta migração não instala triggers, jobs nem procedimentos de calibração.
IF SCHEMA_ID(N'ref') IS NULL THROW 52300, 'Schema ref ausente', 1;
GO
IF OBJECT_ID(N'ref.dt23_politica_suficiencia', N'U') IS NULL
BEGIN
    CREATE TABLE ref.dt23_politica_suficiencia
    (
        politica_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_dt23_politica PRIMARY KEY,
        versao NVARCHAR(80) NOT NULL,
        configuracao_json NVARCHAR(MAX) NOT NULL,
        criada_em DATETIME2(3) NOT NULL CONSTRAINT DF_dt23_politica_criada DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_dt23_politica_versao UNIQUE (versao),
        CONSTRAINT CK_dt23_politica_json CHECK (ISJSON(configuracao_json) = 1),
        CONSTRAINT CK_dt23_politica_versao CHECK (LEN(TRIM(versao)) > 0)
    );
END;
GO
IF OBJECT_ID(N'ref.dt23_diagnostico_suficiencia', N'U') IS NULL
BEGIN
    CREATE TABLE ref.dt23_diagnostico_suficiencia
    (
        diagnostico_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_dt23_diagnostico PRIMARY KEY,
        politica_id UNIQUEIDENTIFIER NOT NULL,
        data_corte DATETIME2(3) NOT NULL,
        estado VARCHAR(20) NOT NULL,
        contadores_json NVARCHAR(MAX) NOT NULL,
        justificativas_json NVARCHAR(MAX) NOT NULL,
        proveniencia_json NVARCHAR(MAX) NOT NULL,
        registrado_em DATETIME2(3) NOT NULL CONSTRAINT DF_dt23_diagnostico_registrado DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_dt23_diagnostico_politica FOREIGN KEY (politica_id)
            REFERENCES ref.dt23_politica_suficiencia(politica_id),
        CONSTRAINT CK_dt23_diagnostico_estado CHECK (estado IN ('VERDE','AMARELO','VERMELHO','INDETERMINADO')),
        CONSTRAINT CK_dt23_diagnostico_json CHECK (
            ISJSON(contadores_json) = 1 AND
            ISJSON(justificativas_json) = 1 AND
            ISJSON(proveniencia_json) = 1)
    );
    CREATE INDEX IX_dt23_diagnostico_corte
        ON ref.dt23_diagnostico_suficiencia (data_corte DESC, registrado_em DESC);
END;
GO
-- A persistência não constitui autorização para gerar, validar ou ativar modelos.
