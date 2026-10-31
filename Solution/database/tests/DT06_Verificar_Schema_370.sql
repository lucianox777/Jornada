-- DT-06: verificador SQL read-only após baseline ou upgrade (sem reset).
SET NOCOUNT ON;
IF OBJECT_ID(N'jornada.schema_migration',N'U') IS NULL
    THROW 51366, 'DT06: ledger ausente.', 1;
IF (SELECT COUNT(*) FROM jornada.schema_migration) <> 75
    THROW 51367, 'DT06: conjunto de migrations incompleto ou inesperado.', 1;
IF EXISTS(SELECT 1 FROM jornada.schema_migration WHERE LEN(sha256)<>64 OR sha256 LIKE '%[^0-9a-f]%')
    THROW 51368, 'DT06: checksum inválido.', 1;
IF COALESCE(CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema')),N'') <> N'3.70'
    THROW 51369, 'DT06: schema esperado 3.70 não confirmado.', 1;
IF OBJECT_ID(N'gold.pessoa',N'U') IS NULL OR OBJECT_ID(N'identidade.identity_map',N'U') IS NULL OR OBJECT_ID(N'bronze.entrega_arquivo',N'U') IS NULL
    THROW 51370, 'DT06: objetos estruturais obrigatórios ausentes.', 1;
-- DT-10 V9 is part of this 75-migration candidate. A successful ledger count
-- alone must not certify an installation missing the governed SQL contract.
IF OBJECT_ID(N'identidade.v9_dt10_lower_policy',N'U') IS NULL
   OR OBJECT_ID(N'auditoria.v9_dt10_lower_policy_autorizacao',N'U') IS NULL
   OR OBJECT_ID(N'auditoria.tr_v9_dt10_lower_policy_autorizacao_append_only',N'TR') IS NULL
   OR OBJECT_ID(N'auditoria.sp_autorizar_v9_dt10_lower_policy',N'P') IS NULL
   OR OBJECT_ID(N'identidade.sp_publicar_resolucao_progressiva_linkage_lote',N'P') IS NULL
    THROW 51371, 'DT06: contrato DT-10 V9 ausente.', 1;
IF COL_LENGTH(N'identidade.v9_dt10_lower_policy',N'habilitada') IS NULL
   OR COL_LENGTH(N'identidade.v9_dt10_lower_policy',N'limiar_inferior') IS NULL
   OR COL_LENGTH(N'identidade.v9_dt10_lower_policy',N'limiar_superior') IS NULL
   OR COL_LENGTH(N'identidade.v9_dt10_lower_policy',N'fit_fingerprint') IS NULL
   OR COL_LENGTH(N'identidade.v9_dt10_lower_policy',N'snapshot_sha256') IS NULL
   OR COL_LENGTH(N'auditoria.v9_dt10_lower_policy_autorizacao',N'politica_sha256') IS NULL
    THROW 51372, 'DT06: colunas criticas DT-10 V9 ausentes.', 1;
IF NOT EXISTS(
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id=dc.parent_object_id
                      AND c.column_id=dc.parent_column_id
    WHERE dc.parent_object_id=OBJECT_ID(N'identidade.v9_dt10_lower_policy',N'U')
      AND c.name=N'habilitada'
      AND REPLACE(REPLACE(REPLACE(dc.definition,N'(',N''),N')',N''),N' ',N'')=N'0')
    THROW 51373, 'DT06: DT-10 V9 sem default OFF verificavel.', 1;
-- Existence alone is insufficient: a disabled append-only trigger would leave
-- the DT-15 attestation mutable while a schema-only preflight still passed.
IF EXISTS(
    SELECT 1 FROM sys.triggers t
    WHERE t.object_id=OBJECT_ID(N'auditoria.tr_v9_dt10_lower_policy_autorizacao_append_only',N'TR')
      AND t.is_disabled=1)
    THROW 51374, 'DT06: trigger append-only DT-15 desabilitada.', 1;
SELECT N'DT06_VERIFY_OK' AS resultado, (SELECT COUNT(*) FROM jornada.schema_migration) AS migrations;
GO
