-- GOV-V9-TEST-REAL-SCHEMA-PREFLIGHT-20261114
-- READ-ONLY structural preflight for a disposable FULL-SCHEMA JornadaHarness.
-- Not a TEST run, not RunAsync parity, not authorization to publish.
-- The existing 4-row synthetic-ledger fixture is NOT a substitute.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'JornadaHarness'
    THROW 52620, 'V9 schema preflight: only disposable JornadaHarness is allowed.', 1;

DECLARE @required TABLE (object_name NVARCHAR(256) NOT NULL, object_kind CHAR(1) NOT NULL);
INSERT @required (object_name, object_kind) VALUES
 (N'silver.pessoa_observacao', 'U'),
 (N'silver.pessoa_atributo_observacao', 'U'),
 (N'identidade.modelo_linkage', 'U'),
 (N'identidade.parametro_linkage', 'U'),
 (N'identidade.frequencia_linkage', 'U'),
 (N'identidade.modelo_linkage_v9_operacional', 'U'),
 (N'identidade.linkage_run', 'U'),
 (N'identidade.linkage_run_item', 'U'),
 (N'identidade.linkage_resultado', 'U'),
 (N'identidade.blocking_chave', 'U'),
 (N'identidade.v_vinculo_corrente', 'V'),
 (N'gold.pessoa', 'U'),
 (N'serving.registro_integrado', 'U');

IF EXISTS (SELECT 1 FROM @required WHERE OBJECT_ID(object_name, object_kind) IS NULL)
    THROW 52621, 'V9 schema preflight: missing required canonical operational objects.', 1;

IF COL_LENGTH('identidade.linkage_run','modelo_id') IS NULL
 OR COL_LENGTH('identidade.linkage_run','status') IS NULL
 OR COL_LENGTH('identidade.linkage_run','registros_elegiveis') IS NULL
 OR COL_LENGTH('identidade.linkage_run','avaliados') IS NULL
 OR COL_LENGTH('identidade.linkage_run','publicado_em') IS NULL
 OR COL_LENGTH('identidade.linkage_resultado','linkage_run_id') IS NULL
 OR COL_LENGTH('identidade.linkage_resultado','modelo_id') IS NULL
 OR COL_LENGTH('identidade.linkage_resultado','pessoa_observacao_id') IS NULL
 OR COL_LENGTH('identidade.linkage_resultado','pessoa_uuid_resolvido') IS NULL
 OR COL_LENGTH('identidade.linkage_resultado','score_melhor') IS NULL
 OR COL_LENGTH('identidade.modelo_linkage_v9_operacional','payload_fitted') IS NULL
 OR COL_LENGTH('identidade.modelo_linkage_v9_operacional','winner_snapshot_sha256') IS NULL
 OR COL_LENGTH('identidade.modelo_linkage_v9_operacional','status') IS NULL
    THROW 52622, 'V9 schema preflight: missing required real-schema columns.', 1;

-- Current legacy RunAsync has an actual FK to legacy modelo_linkage, not
-- to the frozen V9 TEST_ONLY row. The bridge must be proved without
-- injecting a partial pseudo-model or modifying the active model.
IF NOT EXISTS (
 SELECT 1 FROM sys.foreign_key_columns fkc
 JOIN sys.foreign_keys fk ON fk.object_id=fkc.constraint_object_id
 JOIN sys.columns c ON c.object_id=fkc.parent_object_id AND c.column_id=fkc.parent_column_id
 WHERE fk.parent_object_id=OBJECT_ID(N'identidade.linkage_run')
   AND fk.referenced_object_id=OBJECT_ID(N'identidade.modelo_linkage')
   AND c.name=N'modelo_id' AND fk.is_disabled=0 AND fk.is_not_trusted=0
) OR NOT EXISTS (
 SELECT 1 FROM sys.foreign_key_columns fkc
 JOIN sys.foreign_keys fk ON fk.object_id=fkc.constraint_object_id
 JOIN sys.columns c ON c.object_id=fkc.parent_object_id AND c.column_id=fkc.parent_column_id
 WHERE fk.parent_object_id=OBJECT_ID(N'identidade.linkage_resultado')
   AND fk.referenced_object_id=OBJECT_ID(N'identidade.linkage_run')
   AND c.name=N'linkage_run_id' AND fk.is_disabled=0 AND fk.is_not_trusted=0
)
    THROW 52623, 'V9 schema preflight: legacy RunAsync foreign keys not trusted/available.', 1;

IF NOT EXISTS (
 SELECT 1 FROM sys.check_constraints
 WHERE parent_object_id=OBJECT_ID(N'identidade.modelo_linkage_v9_operacional')
   AND name=N'ck_modelo_linkage_v9_operacional_contract'
   AND is_disabled=0 AND is_not_trusted=0
) OR NOT EXISTS (
 SELECT 1 FROM sys.triggers
 WHERE parent_id=OBJECT_ID(N'identidade.modelo_linkage_v9_operacional')
   AND name=N'tr_modelo_linkage_v9_operacional_append_only'
   AND is_disabled=0
)
    THROW 52624, 'V9 schema preflight: immutable TEST_ONLY winner protection absent.', 1;

-- Static view-shape guard only. Row-level nonpublication and Gold/Serving
-- invariance require actual before/after data proofs in a separate test.
DECLARE @viewSql NVARCHAR(MAX) = OBJECT_DEFINITION(
    OBJECT_ID(N'identidade.v_vinculo_corrente', N'V'));
IF @viewSql IS NULL
   OR @viewSql NOT LIKE N'%linkage_run%'
   OR @viewSql NOT LIKE N'%PUBLICADO%'
    THROW 52625, 'V9 schema preflight: published-only view shape unavailable.', 1;

-- The preflight neither reads a sealed TEST corpus nor inserts a linkage run.
SELECT
    N'SCHEMA_PREFLIGHT_PASS_NOT_RUNTIME_PARITY' AS audit_status,
    DB_NAME() AS audited_database,
    N'LEGACY_FK_TO_MODELO_LINKAGE' AS run_model_binding,
    N'V9_TEST_ONLY_IMMUTABLE' AS winner_store,
    N'NOT_EXECUTED' AS runasync_status,
    N'NOT_MEASURED' AS gold_serving_parity,
    N'BLOCKED' AS independent_test_100k,
    CAST(0 AS bit) AS publication_authorized;
