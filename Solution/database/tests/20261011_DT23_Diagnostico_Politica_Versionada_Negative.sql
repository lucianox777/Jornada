-- Fail closed: tests may mutate only dedicated disposable CI databases.
IF DB_NAME() NOT IN (N'JornadaE2E', N'JornadaSyntheticDev')
    THROW 52399, 'DT23 SQL tests require disposable JornadaE2E or JornadaSyntheticDev.', 1;
-- DT23: negative contract checks; run only in disposable SQL CI.
SET NOCOUNT ON;
IF OBJECT_ID(N'ref.dt23_politica_suficiencia', N'U') IS NULL
    THROW 52320, 'DT23 policy table missing', 1;
IF OBJECT_ID(N'ref.dt23_diagnostico_suficiencia', N'U') IS NULL
    THROW 52321, 'DT23 diagnostic table missing', 1;
IF OBJECT_ID(N'ref.vw_dt23_ultimo_diagnostico', N'V') IS NULL
    THROW 52322, 'DT23 read-only projection missing', 1;
IF EXISTS (
    SELECT 1 FROM sys.triggers
    WHERE parent_id IN (OBJECT_ID(N'ref.dt23_politica_suficiencia'),
                        OBJECT_ID(N'ref.dt23_diagnostico_suficiencia'))
)
    THROW 52323, 'DT23 must not install calibration triggers', 1;
IF EXISTS (
    SELECT 1 FROM sys.sql_modules
    WHERE object_id = OBJECT_ID(N'ref.vw_dt23_ultimo_diagnostico')
      AND (UPPER(definition) LIKE '%GENERATE_DRAFT%'
        OR UPPER(definition) LIKE '%ACTIVATE%'
        OR UPPER(definition) LIKE '%VALIDATE%')
)
    THROW 52324, 'DT23 view must not invoke calibration', 1;
-- A policy without a diagnostic must not be presented as certified.
BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @id UNIQUEIDENTIFIER = NEWID();
    INSERT INTO ref.dt23_politica_suficiencia(politica_id, versao, configuracao_json)
    VALUES (@id, CONVERT(NVARCHAR(36), @id), N'{}');
    IF EXISTS (SELECT 1 FROM ref.vw_dt23_ultimo_diagnostico WHERE politica_id = @id)
        THROW 52325, 'Policy without evidence cannot produce a diagnostic', 1;
    -- Under XACT_ABORT ON, an intentional 2627 would doom the transaction.
    -- Assert the enforced uniqueness contract without causing a SQL error.
    IF NOT EXISTS (
        SELECT 1 FROM sys.key_constraints AS k
        WHERE k.parent_object_id = OBJECT_ID(N'ref.dt23_diagnostico_suficiencia')
          AND k.name = N'UQ_dt23_diagnostico_politica_corte'
          AND k.type = 'UQ'
    ) THROW 52326, 'Policy/cutoff uniqueness constraint missing', 1;
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

-- Both domain and provenance guards must be enforced (not merely declared).
IF (SELECT COUNT(*) FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'ref.dt23_diagnostico_suficiencia')
      AND name IN (N'CK_dt23_diagnostico_estado', N'CK_dt23_diagnostico_verde_proveniencia')
      AND is_disabled=0 AND is_not_trusted=0) <> 2
    THROW 52327, 'DT23 diagnostic state/provenance CHECK constraints not trusted', 1;

-- A green status with synthetic provenance is forbidden by the database itself.
-- This is a structural guard, NOT independent verification of a REAL source.
SET XACT_ABORT OFF;
DECLARE @invalid_green_rejected BIT = 0;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @invalid_green_policy UNIQUEIDENTIFIER = NEWID();
    INSERT INTO ref.dt23_politica_suficiencia(politica_id, versao, configuracao_json)
    VALUES (@invalid_green_policy, CONVERT(NVARCHAR(36), @invalid_green_policy), N'{}');
    INSERT INTO ref.dt23_diagnostico_suficiencia
        (diagnostico_id, politica_id, data_corte, estado, contadores_json, justificativas_json, proveniencia_json)
    VALUES (NEWID(), @invalid_green_policy, SYSUTCDATETIME(), 'VERDE',
            N'[]', N'[]', N'{"sourceKind":"SYNTHETIC","verified":false}');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    IF ERROR_NUMBER() <> 547 THROW;
    SET @invalid_green_rejected = 1;
END CATCH;
IF @invalid_green_rejected <> 1
    THROW 52328, 'Synthetic provenance was incorrectly accepted as VERDE', 1;

-- Exhaustive fail-closed negative matrix for persisted state/provenance declarations.
-- Each attempted write uses a fresh synthetic policy and a rolled-back transaction.
-- These checks do not authenticate the real-world origin of evidence.
DECLARE @invalid_cases TABLE (case_id INT PRIMARY KEY, bad_state VARCHAR(20), bad_provenance NVARCHAR(MAX));
INSERT INTO @invalid_cases (case_id, bad_state, bad_provenance) VALUES
(1, 'VERDE', N'{"sourceKind":"INDEPENDENT_REAL","verified":false}'),
(2, 'VERDE', N'{"sourceKind":"INDEPENDENT_REAL"}'),
(3, 'VERDE', N'{"sourceKind":"SYNTHETIC","verified":true}'),
(4, 'INVALID', N'{"sourceKind":"INDEPENDENT_REAL","verified":true}');
DECLARE @case INT = 1;
WHILE @case <= 4
BEGIN
    DECLARE @bad_state VARCHAR(20), @bad_provenance NVARCHAR(MAX), @case_rejected BIT = 0;
    SELECT @bad_state = bad_state, @bad_provenance = bad_provenance
    FROM @invalid_cases WHERE case_id = @case;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @case_policy UNIQUEIDENTIFIER = NEWID();
        INSERT INTO ref.dt23_politica_suficiencia(politica_id, versao, configuracao_json)
        VALUES (@case_policy, CONVERT(NVARCHAR(36), @case_policy), N'{}');
        INSERT INTO ref.dt23_diagnostico_suficiencia
            (diagnostico_id, politica_id, data_corte, estado, contadores_json, justificativas_json, proveniencia_json)
        VALUES (NEWID(), @case_policy, SYSUTCDATETIME(), @bad_state, N'[]', N'[]', @bad_provenance);
        ROLLBACK TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        IF ERROR_NUMBER() <> 547 THROW;
        SET @case_rejected = 1;
    END CATCH;
    IF @case_rejected <> 1
        THROW 52329, 'DT23 persisted invalid state/provenance declaration', 1;
    SET @case += 1;
END;

-- Positive control: the SQL structural guard must not reject the expected JSON
-- shape. This does NOT establish that the independent evidence is authentic.
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @positive_policy UNIQUEIDENTIFIER = NEWID();
    INSERT INTO ref.dt23_politica_suficiencia(politica_id, versao, configuracao_json)
    VALUES (@positive_policy, CONVERT(NVARCHAR(36), @positive_policy), N'{}');
    INSERT INTO ref.dt23_diagnostico_suficiencia
        (diagnostico_id, politica_id, data_corte, estado, contadores_json, justificativas_json, proveniencia_json)
    VALUES (NEWID(), @positive_policy, SYSUTCDATETIME(), 'VERDE', N'[]', N'[]',
            N'{"sourceKind":"INDEPENDENT_REAL","verified":true}');
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
