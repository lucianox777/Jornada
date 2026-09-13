SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Nome da mãe é evidência cadastral/linkage opcional. Ausência permanece NULL;
-- não preencher com string vazia nem rejeitar a observação por este motivo.
IF COL_LENGTH('silver.pessoa_observacao','nome_mae') IS NOT NULL
    ALTER TABLE silver.pessoa_observacao ALTER COLUMN nome_mae NVARCHAR(500) NULL;
IF COL_LENGTH('silver.pessoa_observacao','nome_mae_cmp') IS NOT NULL
    ALTER TABLE silver.pessoa_observacao ALTER COLUMN nome_mae_cmp NVARCHAR(500) NULL;
IF COL_LENGTH('gold.pessoa','nome_mae') IS NOT NULL
    ALTER TABLE gold.pessoa ALTER COLUMN nome_mae NVARCHAR(500) NULL;

COMMIT TRANSACTION;
GO
