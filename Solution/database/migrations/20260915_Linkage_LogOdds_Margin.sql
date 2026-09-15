SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- V6 decide conflitos no espaço de log-odds, antes da sigmoide. A margem persistida
-- deixa, portanto, de ser universalmente uma diferença de probabilidades limitada a 1.
-- A unidade é determinada pela proveniência do modelo associado ao linkage_run:
-- contratos legados persistem diferença de posterior; V6 persiste diferença de log-odds.
-- score_melhor e score_segundo continuam sendo posteriores e permanecem em [0,1].
IF OBJECT_ID(N'identidade.linkage_resultado', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'identidade.linkage_resultado')
          AND name = N'ck_linkage_resultado_scores')
    BEGIN
        ALTER TABLE identidade.linkage_resultado
            DROP CONSTRAINT ck_linkage_resultado_scores;
    END;

    ALTER TABLE identidade.linkage_resultado
        ALTER COLUMN margem DECIMAL(18,8) NULL;

    ALTER TABLE identidade.linkage_resultado WITH CHECK
        ADD CONSTRAINT ck_linkage_resultado_scores CHECK(
            score_melhor >= 0 AND score_melhor <= 1
            AND (score_segundo IS NULL OR (score_segundo >= 0 AND score_segundo <= 1))
            AND (margem IS NULL OR margem >= 0)
        );
END;
GO
