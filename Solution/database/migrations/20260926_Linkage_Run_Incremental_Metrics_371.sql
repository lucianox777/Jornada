SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
 #424 / trem 3.71 aditivo: estoque incremental congelado no mesmo commit de
 linkage_run_item. Historico legado e execucoes FULL/REPLAY/MODEL_VALIDATION
 permanecem NULL: nao se infere o estado anterior pelo resultado atual.
 Nao promove Jornada.SolutionSchema; fechamento coordenado pertence a #411.
*/
IF OBJECT_ID(N'identidade.linkage_run',N'U') IS NULL
 THROW 52071,'Linkage run nao instalado para a telemetria 3.71.',1;
GO
IF COL_LENGTH(N'identidade.linkage_run',N'fresh_pending') IS NULL
 ALTER TABLE identidade.linkage_run ADD fresh_pending BIGINT NULL;
IF COL_LENGTH(N'identidade.linkage_run',N'reavaliados') IS NULL
 ALTER TABLE identidade.linkage_run ADD reavaliados BIGINT NULL;
GO
IF NOT EXISTS (
 SELECT 1 FROM sys.check_constraints
 WHERE parent_object_id=OBJECT_ID(N'identidade.linkage_run')
   AND name=N'ck_linkage_run_universo_incremental'
)
 ALTER TABLE identidade.linkage_run WITH CHECK
 ADD CONSTRAINT ck_linkage_run_universo_incremental CHECK (
   (fresh_pending IS NULL AND reavaliados IS NULL)
   OR (
     tipo_run=N'INCREMENTAL'
     AND fresh_pending IS NOT NULL AND reavaliados IS NOT NULL
     AND fresh_pending>=0 AND reavaliados>=0
     AND fresh_pending+reavaliados=registros_elegiveis
   )
 );
GO
