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
