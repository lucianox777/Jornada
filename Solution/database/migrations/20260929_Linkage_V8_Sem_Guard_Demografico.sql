SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  DC-LK-02 (29/09/2026): novos modelos V8 não persistem nem executam
  SCORING_NON_UNIQUE_DEMOGRAPHIC_EXACT_GUARD_V1. O histórico V6/V7 é preservado.
  Esta migração não altera modelos já persistidos nem promove modelo algum.
*/
CREATE OR ALTER TRIGGER identidade.tr_parametro_linkage_v8_sem_guard_demografico
ON identidade.parametro_linkage
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted p
        JOIN identidade.modelo_linkage m ON m.modelo_id=p.modelo_id
        WHERE m.algoritmo_versao=N'FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8'
          AND p.nome=N'SCORING_NON_UNIQUE_DEMOGRAPHIC_EXACT_GUARD_V1'
    )
        THROW 51036, 'V8 não admite SCORING_NON_UNIQUE_DEMOGRAPHIC_EXACT_GUARD_V1; use evidência FS calibrada e os demais guards.', 1;
END;
GO
