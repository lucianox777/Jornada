-- RF #1936 / fase 2b. Projecao derivada V9 isolada. Nao altera tabela legada.
-- Aplicacao da migracao NAO autoriza DML: C07 exige opt-in DEV e tentativa autenticada.
-- Reaplicar explicitamente apos reversao restaura apenas disponibilidade do schema.
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
DECLARE @lock_result int;
EXEC @lock_result=sys.sp_getapplock @Resource=N'Jornada.V9.BlockingSchema',
    @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
IF @lock_result<0 THROW 52960,'V9 blocking schema lock unavailable.',1;

IF OBJECT_ID(N'identidade.blocking_chave_v9',N'U') IS NULL
BEGIN
    CREATE TABLE identidade.blocking_chave_v9(
        blocking_chave_id bigint IDENTITY(1,1) NOT NULL,
        pessoa_uuid uniqueidentifier NOT NULL,
        normalizacao_versao nvarchar(80) NOT NULL,
        projection_schema_version nvarchar(120) NOT NULL,
        projection_fingerprint_sha256 char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        atributo nvarchar(80) NOT NULL,
        valor_normalizado nvarchar(500) NOT NULL,
        semantica_temporal nvarchar(30) NOT NULL,
        vigencia_inicio datetimeoffset(7) NOT NULL,
        vigencia_fim datetimeoffset(7) NULL,
        gerado_em datetimeoffset(7) NOT NULL CONSTRAINT DF_blocking_chave_v9_gerado DEFAULT(SYSDATETIMEOFFSET()),
        CONSTRAINT PK_blocking_chave_v9 PRIMARY KEY CLUSTERED(blocking_chave_id),
        -- Exact logical key, <= 1650 bytes; no lossy truncation or hash substitution.
        CONSTRAINT UQ_blocking_chave_v9_logical UNIQUE NONCLUSTERED(
            pessoa_uuid,normalizacao_versao,projection_schema_version,
            projection_fingerprint_sha256,atributo,valor_normalizado,vigencia_inicio),
        CONSTRAINT FK_blocking_chave_v9_pessoa FOREIGN KEY(pessoa_uuid)
            REFERENCES identidade.pessoa(pessoa_uuid),
        CONSTRAINT CK_blocking_chave_v9_normalizacao CHECK(LEN(LTRIM(RTRIM(normalizacao_versao)))>0),
        CONSTRAINT CK_blocking_chave_v9_schema CHECK(projection_schema_version=N'V9_CALIBRATION_BLOCKING_PROJECTION_V1'),
        CONSTRAINT CK_blocking_chave_v9_fingerprint CHECK(LEN(projection_fingerprint_sha256)=64
            AND projection_fingerprint_sha256 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_blocking_chave_v9_atributo CHECK(LEN(LTRIM(RTRIM(atributo)))>0),
        CONSTRAINT CK_blocking_chave_v9_valor CHECK(LEN(LTRIM(RTRIM(valor_normalizado)))>0),
        CONSTRAINT CK_blocking_chave_v9_temporal CHECK(semantica_temporal IN('STABLE_IDENTITY_DATUM','VERSIONED_ALIAS')),
        CONSTRAINT CK_blocking_chave_v9_vigencia CHECK(vigencia_fim IS NULL OR vigencia_fim>=vigencia_inicio)
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'identidade.blocking_chave_v9')
              AND name=N'IX_blocking_chave_v9_lookup')
    CREATE INDEX IX_blocking_chave_v9_lookup ON identidade.blocking_chave_v9(
        normalizacao_versao,projection_schema_version,projection_fingerprint_sha256,
        atributo,valor_normalizado,vigencia_fim,pessoa_uuid);

IF OBJECT_ID(N'identidade.blocking_chave_v9_cobertura',N'U') IS NULL
BEGIN
    CREATE TABLE identidade.blocking_chave_v9_cobertura(
        projection_fingerprint_sha256 char(64) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY,
        inicializado_em datetimeoffset(7) NOT NULL,
        CONSTRAINT CK_blocking_chave_v9_cobertura_sha CHECK(LEN(projection_fingerprint_sha256)=64
            AND projection_fingerprint_sha256 NOT LIKE '%[^0-9a-f]%')
    );
END;

-- Distinguish never initialized from a corrupted/incomplete index. A receipt
-- is written only inside the same transaction as a complete reconciliation.
IF OBJECT_ID(N'identidade.blocking_chave_v9_cobertura',N'U') IS NULL
BEGIN
    CREATE TABLE identidade.blocking_chave_v9_cobertura(
        normalizacao_versao nvarchar(80) NOT NULL,
        projection_schema_version nvarchar(120) NOT NULL,
        projection_fingerprint_sha256 char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        inicializado_em datetimeoffset(7) NOT NULL CONSTRAINT DF_blocking_chave_v9_cobertura_inicio DEFAULT(SYSDATETIMEOFFSET()),
        CONSTRAINT PK_blocking_chave_v9_cobertura PRIMARY KEY(
            normalizacao_versao,projection_schema_version,projection_fingerprint_sha256),
        CONSTRAINT CK_blocking_chave_v9_cobertura_schema CHECK(projection_schema_version=N'V9_CALIBRATION_BLOCKING_PROJECTION_V1'),
        CONSTRAINT CK_blocking_chave_v9_cobertura_fingerprint CHECK(LEN(projection_fingerprint_sha256)=64
            AND projection_fingerprint_sha256 NOT LIKE '%[^0-9a-f]%')
    );
END;

-- Schema availability only: no writes to projection rows, no backfill, no authorization.
IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.V9.BlockingSchemaEnabled')
    EXEC sys.sp_updateextendedproperty @name=N'Jornada.V9.BlockingSchemaEnabled',@value=N'1';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'Jornada.V9.BlockingSchemaEnabled',@value=N'1';
COMMIT TRANSACTION;
GO
