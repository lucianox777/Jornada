-- Jornada SQL Server: identidade física explícita da projeção de blocking.
-- Não retrocarimba linhas/rulesets históricos: NULL/NULL significa legado sem linhagem comprovada.
-- Novas blocking_chave recebem o contrato corrente por DEFAULT; novos rulesets gravam o par explicitamente.

IF COL_LENGTH('identidade.blocking_chave','projection_schema_version') IS NULL
    ALTER TABLE identidade.blocking_chave ADD projection_schema_version NVARCHAR(120) NULL;
IF COL_LENGTH('identidade.blocking_chave','projection_fingerprint_sha256') IS NULL
    ALTER TABLE identidade.blocking_chave ADD projection_fingerprint_sha256 CHAR(64) NULL;
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
    WHERE dc.parent_object_id=OBJECT_ID('identidade.blocking_chave') AND c.name='projection_schema_version')
    ALTER TABLE identidade.blocking_chave
        ADD CONSTRAINT DF_blocking_chave_projection_schema DEFAULT('PERSON_RESOLUTION_PROJECTION_V2') FOR projection_schema_version;
IF NOT EXISTS(
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
    WHERE dc.parent_object_id=OBJECT_ID('identidade.blocking_chave') AND c.name='projection_fingerprint_sha256')
    ALTER TABLE identidade.blocking_chave
        ADD CONSTRAINT DF_blocking_chave_projection_fingerprint DEFAULT('d186f28c51e18802f7c2df5b8b192b6278d28874833c8d824d483608aa3abbb4') FOR projection_fingerprint_sha256;
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID('identidade.blocking_chave') AND name='CK_blocking_chave_projection_pair')
    ALTER TABLE identidade.blocking_chave WITH CHECK ADD CONSTRAINT CK_blocking_chave_projection_pair CHECK(
        (projection_schema_version IS NULL AND projection_fingerprint_sha256 IS NULL)
        OR (projection_schema_version IS NOT NULL AND LEN(LTRIM(RTRIM(projection_schema_version)))>0
            AND projection_fingerprint_sha256 IS NOT NULL AND LEN(projection_fingerprint_sha256)=64));
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID('identidade.blocking_chave') AND name='IX_blocking_chave_projection_lookup')
    CREATE INDEX IX_blocking_chave_projection_lookup
        ON identidade.blocking_chave(
            normalizacao_versao,projection_schema_version,projection_fingerprint_sha256,
            atributo,valor_normalizado,vigencia_fim,pessoa_uuid);
GO

IF COL_LENGTH('identidade.linkage_ruleset','projection_schema_version') IS NULL
    ALTER TABLE identidade.linkage_ruleset ADD projection_schema_version NVARCHAR(120) NULL;
IF COL_LENGTH('identidade.linkage_ruleset','projection_fingerprint_sha256') IS NULL
    ALTER TABLE identidade.linkage_ruleset ADD projection_fingerprint_sha256 CHAR(64) NULL;
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID('identidade.linkage_ruleset') AND name='CK_linkage_ruleset_projection_pair')
    ALTER TABLE identidade.linkage_ruleset WITH CHECK ADD CONSTRAINT CK_linkage_ruleset_projection_pair CHECK(
        (projection_schema_version IS NULL AND projection_fingerprint_sha256 IS NULL)
        OR (projection_schema_version IS NOT NULL AND LEN(LTRIM(RTRIM(projection_schema_version)))>0
            AND projection_fingerprint_sha256 IS NOT NULL AND LEN(projection_fingerprint_sha256)=64));
GO
