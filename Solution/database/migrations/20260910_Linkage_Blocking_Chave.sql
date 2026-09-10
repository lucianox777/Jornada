-- Jornada SQL Server: projeção operacional derivada para blocking dinâmico.
-- Não é nova verdade cadastral. Pode ser reconstruída a partir da Gold/histórico e da versão de normalização.
-- A estrutura estável evita DDL por versão de ruleset.
-- Nomes são aliases versionáveis; nascimento é dado estável e sua alteração representa correção excepcional.

IF OBJECT_ID('identidade.blocking_chave','U') IS NULL
BEGIN
    CREATE TABLE identidade.blocking_chave(
        pessoa_uuid UNIQUEIDENTIFIER NOT NULL,
        normalizacao_versao NVARCHAR(80) NOT NULL,
        atributo NVARCHAR(80) NOT NULL,
        valor_normalizado NVARCHAR(500) NOT NULL,
        semantica_temporal NVARCHAR(30) NOT NULL,
        vigencia_inicio DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_blocking_chave_vigencia_inicio DEFAULT(SYSDATETIMEOFFSET()),
        vigencia_fim DATETIMEOFFSET(7) NULL,
        gerado_em DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_blocking_chave_gerado_em DEFAULT(SYSDATETIMEOFFSET()),
        CONSTRAINT PK_blocking_chave PRIMARY KEY(pessoa_uuid,normalizacao_versao,atributo,valor_normalizado,vigencia_inicio),
        CONSTRAINT FK_blocking_chave_pessoa FOREIGN KEY(pessoa_uuid) REFERENCES identidade.pessoa(pessoa_uuid),
        CONSTRAINT CK_blocking_chave_normalizacao CHECK(LEN(LTRIM(RTRIM(normalizacao_versao)))>0),
        CONSTRAINT CK_blocking_chave_atributo CHECK(LEN(LTRIM(RTRIM(atributo)))>0),
        CONSTRAINT CK_blocking_chave_valor CHECK(LEN(LTRIM(RTRIM(valor_normalizado)))>0),
        CONSTRAINT CK_blocking_chave_semantica CHECK(semantica_temporal IN('STABLE_IDENTITY_DATUM','VERSIONED_ALIAS')),
        CONSTRAINT CK_blocking_chave_vigencia CHECK(vigencia_fim IS NULL OR vigencia_fim>=vigencia_inicio)
    );
END;
GO

IF NOT EXISTS(
    SELECT 1
    FROM sys.indexes
    WHERE object_id=OBJECT_ID('identidade.blocking_chave')
      AND name='IX_blocking_chave_lookup')
BEGIN
    CREATE INDEX IX_blocking_chave_lookup
        ON identidade.blocking_chave(normalizacao_versao,atributo,valor_normalizado,vigencia_fim,pessoa_uuid);
END;
GO
