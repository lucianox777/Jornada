-- V5: a margem entre candidatos passa a operar no espaço de log-odds antes da sigmoide.
-- A ampliação é monotônica e sem perda para linhas legadas, cuja margem histórica permanece <= 1.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('identidade.linkage_resultado','U') IS NOT NULL
   AND COL_LENGTH('identidade.linkage_resultado','margem') IS NOT NULL
BEGIN
    ALTER TABLE identidade.linkage_resultado ALTER COLUMN margem DECIMAL(19,8) NULL;
END;

COMMIT TRANSACTION;
