:on error exit
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @page_size INT = $(PAGE_SIZE);
IF @page_size IS NULL OR @page_size < 1 OR @page_size > 1000
    THROW 51160,'PAGE_SIZE do backfill progressivo local deve estar entre 1 e 1000.',1;

IF OBJECT_ID('silver.pessoa_origem','U') IS NULL
   OR OBJECT_ID('identidade.pessoa_origem_progressiva','U') IS NULL
   OR OBJECT_ID('identidade.sp_assegurar_origem_progressiva','P') IS NULL
    THROW 51161,'Pré-requisitos do backfill progressivo local não instalados.',1;

DECLARE @origens TABLE(pessoa_origem_id BIGINT NOT NULL PRIMARY KEY);
INSERT INTO @origens(pessoa_origem_id)
SELECT TOP (@page_size) o.pessoa_origem_id
FROM silver.pessoa_origem o
LEFT JOIN identidade.pessoa_origem_progressiva p
  ON p.pessoa_origem_id=o.pessoa_origem_id
WHERE p.pessoa_origem_id IS NULL
ORDER BY o.pessoa_origem_id;

DECLARE @pessoa_origem_id BIGINT;
DECLARE @resultado TABLE(
    initial_uuid UNIQUEIDENTIFIER NOT NULL,
    legacy_pessoa_uuid UNIQUEIDENTIFIER NULL,
    estado VARCHAR(20) NOT NULL,
    versao BIGINT NOT NULL
);
DECLARE origens_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT pessoa_origem_id FROM @origens ORDER BY pessoa_origem_id;

OPEN origens_cursor;
FETCH NEXT FROM origens_cursor INTO @pessoa_origem_id;
WHILE @@FETCH_STATUS=0
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        DELETE FROM @resultado;
        INSERT INTO @resultado(initial_uuid,legacy_pessoa_uuid,estado,versao)
            EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@pessoa_origem_id;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
    FETCH NEXT FROM origens_cursor INTO @pessoa_origem_id;
END;
CLOSE origens_cursor;
DEALLOCATE origens_cursor;

DECLARE @processadas BIGINT;
SELECT @processadas=COUNT_BIG(*) FROM @origens;
PRINT CONCAT('BACKFILL PROGRESSIVO LOCAL: processadas=', @processadas);
