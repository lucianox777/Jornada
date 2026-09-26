-- DT-06 / 2026-09-26: baseline nova do SolutionSchema 3.70.
-- Manifesto fixado em 42470cc1aa2166435e6776948a84b4baf824c0d7; SHA256(manifest.txt) 78f2b3274bce456a4248455662393e4b19361e9f1f0c67792ed43ce148036e9a.
-- Pré-requisito obrigatório: cd Solution/database && sha256sum -c baselines/DT06_SHA256SUMS.txt
-- Destino precisa ser banco EXISTENTE e VAZIO. Nunca usar para upgrade de banco existente.
:on error exit
SET NOCOUNT ON;
IF EXISTS(SELECT 1 FROM sys.tables WHERE is_ms_shipped=0)
    THROW 51365, 'DT06: banco não está vazio; baseline abortada sem reset.', 1;
GO
:r database/Jornada_Fase1_v3.70.sql
:r database/baselines/DT06_Baseline_Ledger_370.sql
