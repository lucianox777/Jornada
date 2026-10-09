-- C3.2f2c PRIVATE SQL ONLY.
-- NEVER apply to JornadaLocal, HML, PROD, IBGE, or the normal NODE Compose.
-- Invoke only after dedicated GitHub E2E bootstrap with sqlcmd -v
-- RecoveryKey="ci-e2e-recovery-<GITHUB_RUN_ID>-<GITHUB_RUN_ATTEMPT>".
-- This file is INERT until the private CI harness explicitly invokes it.
SET NOCOUNT ON;
IF DB_NAME() <> N'JornadaE2E'
    THROW 51801, 'C3.2f2c trigger may only exist in JornadaE2E', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 0 AND name = N'Jornada.EnvironmentProfile'
      AND CONVERT(NVARCHAR(100), value) = N'Development'
) THROW 51802, 'C3.2f2c requires disposable Development SQL', 1;
IF N'$(RecoveryKey)' NOT LIKE N'ci-e2e-recovery-[0-9]%'
    THROW 51803, 'C3.2f2c expected synthetic CI-only key', 1;
IF OBJECT_ID(N'identidade.vinculo_fonte', N'U') IS NULL
    THROW 51804, 'C3.2f2c identity link table is absent', 1;
GO

-- The Processor INSERT into Silver uses OUTPUT INSERTED without INTO, so a
-- trigger on Silver would BREAK it (SQL Server disallows OUTPUT on a table
-- with an enabled trigger). Instead attach to identidade.vinculo_fonte,
-- which the Processor inserts without OUTPUT later in the SAME transaction.
-- Hold that transaction open so a second observer can verify the dirty row
-- AND the committed PROCESSANDO lease, BEFORE the CI injects SIGKILL.
-- Second attempt is briefly held to allow observing the new fencing token.
-- No other delivery is delayed; matching is by exact CI-only idempotency key.
CREATE OR ALTER TRIGGER identidade.tr_ci_c3_2f2c_hold_first_write
ON identidade.vinculo_fonte
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF DB_NAME() <> N'JornadaE2E'
        THROW 51805, 'C3.2f2c trigger outside private E2E', 1;

    DECLARE @attempt INT = NULL;
    SELECT TOP (1) @attempt = l.tentativa_count
    FROM inserted i
    JOIN silver.pessoa_observacao p ON p.pessoa_observacao_id = i.pessoa_observacao_id
    JOIN ingestao.lote l ON l.lote_id = p.lote_id
    JOIN ingestao.entrega e ON e.entrega_id = l.entrega_id
    WHERE e.idempotency_key = N'$(RecoveryKey)';

    IF @attempt = 1 WAITFOR DELAY '00:01:30';
    ELSE IF @attempt >= 2 WAITFOR DELAY '00:00:15';
END;
GO

-- Installation evidence: exactly one trigger, no trigger on any other table.
IF (
    SELECT COUNT(*)
    FROM sys.triggers
    WHERE name = N'tr_ci_c3_2f2c_hold_first_write'
      AND parent_id = OBJECT_ID(N'identidade.vinculo_fonte')
      AND is_disabled = 0
) <> 1
    THROW 51806, 'C3.2f2c hold trigger not installed', 1;
GO
