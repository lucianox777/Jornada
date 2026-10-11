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
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
