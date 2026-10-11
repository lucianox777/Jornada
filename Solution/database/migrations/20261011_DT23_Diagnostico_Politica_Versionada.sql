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
        CONSTRAINT CK_dt23_diagnostico_verde_proveniencia CHECK (estado <> 'VERDE' OR (ISNULL(JSON_VALUE(proveniencia_json, '$.sourceKind'), N'') = N'INDEPENDENT_REAL' AND ISNULL(JSON_VALUE(proveniencia_json, '$.verified'), N'') = N'true')),
        CONSTRAINT UQ_dt23_diagnostico_politica_corte UNIQUE (politica_id, data_corte),
        CONSTRAINT CK_dt23_diagnostico_json CHECK (
            ISJSON(contadores_json) = 1 AND
            ISJSON(justificativas_json) = 1 AND
            ISJSON(proveniencia_json) = 1)
    );
    CREATE INDEX IX_dt23_diagnostico_corte
        ON ref.dt23_diagnostico_suficiencia (data_corte DESC, registrado_em DESC);
END;
GO
-- Reentry must validate existing policy records, not only fresh installations.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'ref.dt23_politica_suficiencia')
                 AND name = N'CK_dt23_politica_json')
    ALTER TABLE ref.dt23_politica_suficiencia WITH CHECK
        ADD CONSTRAINT CK_dt23_politica_json CHECK (ISJSON(configuracao_json) = 1);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'ref.dt23_politica_suficiencia')
                 AND name = N'CK_dt23_politica_versao')
    ALTER TABLE ref.dt23_politica_suficiencia WITH CHECK
        ADD CONSTRAINT CK_dt23_politica_versao CHECK (LEN(TRIM(versao)) > 0);
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE parent_object_id = OBJECT_ID(N'ref.dt23_politica_suficiencia')
             AND name IN (N'CK_dt23_politica_json', N'CK_dt23_politica_versao')
             AND (is_disabled = 1 OR is_not_trusted = 1))
    THROW 52303, 'DT23 policy CHECK constraint disabled or untrusted; reconcile schema', 1;
GO
-- A repetição do mesmo ciclo não deve criar snapshots divergentes.
-- Falha fechada quando a mesma política/corte já existe: o produtor deve
-- comparar o snapshot existente antes de decidir reutilizá-lo.
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'ref.dt23_diagnostico_suficiencia') AND name=N'UQ_dt23_diagnostico_politica_corte')
BEGIN
    IF EXISTS (SELECT 1 FROM ref.dt23_diagnostico_suficiencia GROUP BY politica_id, data_corte HAVING COUNT_BIG(*)>1)
        THROW 52301, 'Diagnosticos duplicados por politica e corte; reconciliar antes de instalar unicidade', 1;
    ALTER TABLE ref.dt23_diagnostico_suficiencia ADD CONSTRAINT UQ_dt23_diagnostico_politica_corte UNIQUE (politica_id, data_corte);
END;
GO
-- Reentry on an existing DT23 schema must not silently retain an unchecked state
-- domain or accept a VERDE declaration explicitly marked synthetic/unverified.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id=OBJECT_ID(N'ref.dt23_diagnostico_suficiencia')
                 AND name=N'CK_dt23_diagnostico_estado')
    ALTER TABLE ref.dt23_diagnostico_suficiencia WITH CHECK
        ADD CONSTRAINT CK_dt23_diagnostico_estado
        CHECK (estado IN ('VERDE','AMARELO','VERMELHO','INDETERMINADO'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id=OBJECT_ID(N'ref.dt23_diagnostico_suficiencia')
                 AND name=N'CK_dt23_diagnostico_verde_proveniencia')
    ALTER TABLE ref.dt23_diagnostico_suficiencia WITH CHECK
        ADD CONSTRAINT CK_dt23_diagnostico_verde_proveniencia CHECK (estado <> 'VERDE' OR (ISNULL(JSON_VALUE(proveniencia_json, '$.sourceKind'), N'') = N'INDEPENDENT_REAL' AND ISNULL(JSON_VALUE(proveniencia_json, '$.verified'), N'') = N'true'));
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE parent_object_id=OBJECT_ID(N'ref.dt23_diagnostico_suficiencia')
             AND name IN (N'CK_dt23_diagnostico_estado', N'CK_dt23_diagnostico_verde_proveniencia')
             AND (is_disabled=1 OR is_not_trusted=1))
    THROW 52302, 'DT23 diagnostic CHECK constraint disabled or untrusted; reconcile schema', 1;
GO
-- Consultas periódicas por política e corte não devem varrer todo o histórico.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'ref.dt23_diagnostico_suficiencia') AND name=N'IX_dt23_diagnostico_politica_recente')
    CREATE INDEX IX_dt23_diagnostico_politica_recente
        ON ref.dt23_diagnostico_suficiencia (politica_id, registrado_em DESC, diagnostico_id DESC);
GO
-- Operational cycles must not overwrite a previous cutoff, including concurrent retries.
-- Writers compare an existing snapshot with their proposed immutable payload and
-- return an explicit conflict rather than replacing an earlier observation.
GO
CREATE OR ALTER PROCEDURE ref.sp_dt23_registrar_diagnostico
    @diagnostico_id UNIQUEIDENTIFIER,
    @politica_id UNIQUEIDENTIFIER,
    @data_corte DATETIME2(3),
    @estado VARCHAR(20),
    @contadores_json NVARCHAR(MAX),
    @justificativas_json NVARCHAR(MAX),
    @proveniencia_json NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @ambient_transaction BIT = CASE WHEN @@TRANCOUNT > 0 THEN 1 ELSE 0 END;
    IF @ambient_transaction = 1 SAVE TRANSACTION dt23_diagnostic_write;
    ELSE BEGIN TRANSACTION;
    DECLARE @existing UNIQUEIDENTIFIER;
    SELECT @existing = diagnostico_id
    FROM ref.dt23_diagnostico_suficiencia WITH (UPDLOCK, HOLDLOCK)
    WHERE politica_id = @politica_id AND data_corte = @data_corte;
    IF @existing IS NOT NULL
    BEGIN
        IF EXISTS (
            SELECT 1 FROM ref.dt23_diagnostico_suficiencia
            WHERE diagnostico_id = @existing AND diagnostico_id = @diagnostico_id
              AND estado = @estado AND contadores_json = @contadores_json
              AND justificativas_json = @justificativas_json
              AND proveniencia_json = @proveniencia_json
        )
        BEGIN
            IF @ambient_transaction = 0 COMMIT TRANSACTION;
            RETURN;
        END;
        IF @ambient_transaction = 1 ROLLBACK TRANSACTION dt23_diagnostic_write;
        ELSE ROLLBACK TRANSACTION;
        THROW 52304, 'DT23 cutoff already has a different immutable diagnostic', 1;
    END;
    INSERT INTO ref.dt23_diagnostico_suficiencia
        (diagnostico_id, politica_id, data_corte, estado, contadores_json,
         justificativas_json, proveniencia_json)
    VALUES (@diagnostico_id, @politica_id, @data_corte, @estado, @contadores_json,
            @justificativas_json, @proveniencia_json);
    IF @ambient_transaction = 0 COMMIT TRANSACTION;
END;
GO
-- A persistência não constitui autorização para gerar, validar ou ativar modelos.

-- Snapshot mais recente por política: projeção estritamente read-only para consumidores operacionais.
-- Sem diagnóstico registrado, nenhuma linha é emitida (não sintetizar VERDE).
CREATE OR ALTER VIEW ref.vw_dt23_ultimo_diagnostico
AS
SELECT d.diagnostico_id, d.politica_id, p.versao AS politica_versao,
       d.data_corte, d.estado, d.contadores_json, d.justificativas_json,
       d.proveniencia_json, d.registrado_em
FROM ref.dt23_diagnostico_suficiencia AS d
INNER JOIN ref.dt23_politica_suficiencia AS p ON p.politica_id = d.politica_id
WHERE NOT EXISTS (
    SELECT 1 FROM ref.dt23_diagnostico_suficiencia AS mais_recente
    WHERE mais_recente.politica_id = d.politica_id
      AND (mais_recente.data_corte > d.data_corte
        OR (mais_recente.data_corte = d.data_corte
          AND (mais_recente.registrado_em > d.registrado_em
            OR (mais_recente.registrado_em = d.registrado_em
              AND mais_recente.diagnostico_id > d.diagnostico_id))))
);
GO
