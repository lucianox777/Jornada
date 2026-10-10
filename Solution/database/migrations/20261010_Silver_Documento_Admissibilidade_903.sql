SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO
-- #903: FK de cobertura garante existencia, mas nao garante admissibilidade.
-- Validar em INSERT/UPDATE sem tornar documento ainda nao validado uma prova Gold.
CREATE OR ALTER TRIGGER silver.tr_documento_evidencia_valor_admissivel
ON silver.documento_evidencia_valor
AFTER INSERT,UPDATE
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(
   SELECT 1 FROM inserted i
   JOIN ref.modelo_documento_atributo a
     ON a.modelo_documento_id=i.modelo_documento_id
    AND a.atributo_codigo=i.atributo_codigo
   WHERE a.comprovacao_admissivel=0
 )
   THROW 52318,'Atributo nao admissivel como comprovacao no modelo documental.',1;
END;
GO
