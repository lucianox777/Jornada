SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Ledger canônico das migrações que compõem o SolutionSchema corrente.
-- Este arquivo é o primeiro item do manifesto e precisa ser idempotente porque
-- o executor de upgrade o usa para bootstrap antes de consultar os checksums.
IF SCHEMA_ID(N'jornada') IS NULL
    EXEC(N'CREATE SCHEMA jornada');
GO

IF OBJECT_ID(N'jornada.schema_migration', N'U') IS NULL
BEGIN
    CREATE TABLE jornada.schema_migration(
        migration_name NVARCHAR(260) NOT NULL
            CONSTRAINT pk_jornada_schema_migration PRIMARY KEY,
        sha256 CHAR(64) NOT NULL,
        applied_at DATETIME2(3) NOT NULL
            CONSTRAINT df_jornada_schema_migration_applied_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT ck_jornada_schema_migration_sha256 CHECK(
            LEN(sha256)=64 AND sha256 NOT LIKE '%[^0-9a-f]%')
    );
END;
GO
