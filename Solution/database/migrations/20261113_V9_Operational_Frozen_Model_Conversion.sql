SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

-- Versioned, additive V9 operational model contract.
-- The legacy operational tables cannot encode the 81-cell joint FS model,
-- all four TF/U frozen dictionaries and blind FS grouping without loss.
-- Store the ENTIRE canonical fitted byte payload; do NOT map to legacy rows
-- and quietly discard calibration evidence. The production Runner V9 loader
-- and the independent TEST share this exact SQL contract and verifier.
-- No activation, migration of active linkage, publication or DT-15 here.
IF SCHEMA_ID(N'identidade') IS NULL EXEC(N'CREATE SCHEMA identidade');
GO
IF OBJECT_ID(N'identidade.modelo_linkage_v9_operacional',N'U') IS NULL
BEGIN
    CREATE TABLE identidade.modelo_linkage_v9_operacional
    (
        modelo_id UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT pk_modelo_linkage_v9_operacional PRIMARY KEY,
        contrato NVARCHAR(80) NOT NULL,
        snapshot_schema NVARCHAR(80) NOT NULL,
        winner_snapshot_sha256 CHAR(64) NOT NULL,
        fit_fingerprint_sha256 CHAR(64) NOT NULL,
        projection_fingerprint_sha256 CHAR(64) NOT NULL,
        joint_cell_count INT NOT NULL,
        tf_slot_count INT NOT NULL,
        blocking_pass_count INT NOT NULL,
        payload_fitted VARBINARY(MAX) NOT NULL,
        status NVARCHAR(20) NOT NULL,
        criado_em DATETIME2(3) NOT NULL
            CONSTRAINT df_modelo_linkage_v9_operacional_criado DEFAULT SYSUTCDATETIME(),
        CONSTRAINT uq_modelo_linkage_v9_operacional_sha
            UNIQUE(winner_snapshot_sha256),
        CONSTRAINT ck_modelo_linkage_v9_operacional_contract
            CHECK (contrato=N'JORNADA_V9_OPERATIONAL_SQL_MODEL_V1'
                AND snapshot_schema=N'V9_FITTED_SNAPSHOT_V3'
                AND status=N'TEST_ONLY'
                AND joint_cell_count=81
                AND tf_slot_count=4
                AND blocking_pass_count>0
                AND DATALENGTH(payload_fitted)>0
                AND DATALENGTH(payload_fitted)<=67108864)
    );
END;
GO
-- Immutable by database enforcement; callers can INSERT ONCE, not UPDATE or
-- DELETE an existing V9 operational winner. No implicit active model.
CREATE OR ALTER TRIGGER identidade.tr_modelo_linkage_v9_operacional_append_only
ON identidade.modelo_linkage_v9_operacional
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52417, 'Modelo V9 congelado: UPDATE/DELETE proibido; nova versao exige novo SHA/modelo.', 1;
END;
GO
