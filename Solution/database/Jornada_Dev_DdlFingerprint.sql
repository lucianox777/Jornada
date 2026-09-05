SET NOCOUNT ON;

-- O catálogo do SQL Server pode usar collation de catálogo diferente da collation
-- do banco. Normalizamos explicitamente todos os fragmentos textuais antes do
-- CONCAT para que o fingerprint seja reproduzível também ao validar baselines
-- criados com collations legadas.
WITH metadata_lines AS (
    SELECT CONCAT(
        N'COLUMN|',
        s.name COLLATE DATABASE_DEFAULT,N'|',
        t.name COLLATE DATABASE_DEFAULT,N'|',
        c.column_id,N'|',
        c.name COLLATE DATABASE_DEFAULT,N'|',
        ty.name COLLATE DATABASE_DEFAULT,N'|',
        c.max_length,N'|',c.precision,N'|',c.scale,N'|',c.is_nullable,N'|',c.is_identity,N'|',
        COALESCE(dc.definition,N'') COLLATE DATABASE_DEFAULT) AS line
    FROM sys.tables t
    JOIN sys.schemas s ON s.schema_id=t.schema_id
    JOIN sys.columns c ON c.object_id=t.object_id
    JOIN sys.types ty ON ty.user_type_id=c.user_type_id
    LEFT JOIN sys.default_constraints dc ON dc.parent_object_id=c.object_id AND dc.parent_column_id=c.column_id
    WHERE s.name NOT IN('sys','INFORMATION_SCHEMA')
    UNION ALL
    SELECT CONCAT(
        N'INDEX|',
        s.name COLLATE DATABASE_DEFAULT,N'|',
        t.name COLLATE DATABASE_DEFAULT,N'|',
        i.name COLLATE DATABASE_DEFAULT,N'|',
        i.is_unique,N'|',i.type_desc COLLATE DATABASE_DEFAULT,N'|',ic.key_ordinal,N'|',ic.is_descending_key,N'|',ic.is_included_column,N'|',
        c.name COLLATE DATABASE_DEFAULT,N'|',
        COALESCE(i.filter_definition,N'') COLLATE DATABASE_DEFAULT)
    FROM sys.tables t
    JOIN sys.schemas s ON s.schema_id=t.schema_id
    JOIN sys.indexes i ON i.object_id=t.object_id AND i.index_id>0 AND i.is_hypothetical=0
    JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE s.name NOT IN('sys','INFORMATION_SCHEMA')
    UNION ALL
    SELECT CONCAT(
        N'OBJECT|',
        s.name COLLATE DATABASE_DEFAULT,N'|',
        o.name COLLATE DATABASE_DEFAULT,N'|',
        o.type COLLATE DATABASE_DEFAULT,N'|',
        CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),COALESCE(m.definition,N''))),2) COLLATE DATABASE_DEFAULT)
    FROM sys.objects o
    JOIN sys.schemas s ON s.schema_id=o.schema_id
    LEFT JOIN sys.sql_modules m ON m.object_id=o.object_id
    WHERE o.is_ms_shipped=0 AND o.type IN('V','P','TR','FN','IF','TF')
    UNION ALL
    SELECT CONCAT(
        N'CHECK|',
        s.name COLLATE DATABASE_DEFAULT,N'|',
        t.name COLLATE DATABASE_DEFAULT,N'|',
        cc.name COLLATE DATABASE_DEFAULT,N'|',
        cc.is_disabled,N'|',cc.is_not_trusted,N'|',
        cc.definition COLLATE DATABASE_DEFAULT)
    FROM sys.check_constraints cc
    JOIN sys.tables t ON t.object_id=cc.parent_object_id
    JOIN sys.schemas s ON s.schema_id=t.schema_id
    UNION ALL
    SELECT CONCAT(
        N'FK|',
        s.name COLLATE DATABASE_DEFAULT,N'|',
        t.name COLLATE DATABASE_DEFAULT,N'|',
        fk.name COLLATE DATABASE_DEFAULT,N'|',
        fk.is_disabled,N'|',fk.is_not_trusted,N'|',
        OBJECT_SCHEMA_NAME(fk.referenced_object_id) COLLATE DATABASE_DEFAULT,N'|',
        OBJECT_NAME(fk.referenced_object_id) COLLATE DATABASE_DEFAULT)
    FROM sys.foreign_keys fk
    JOIN sys.tables t ON t.object_id=fk.parent_object_id
    JOIN sys.schemas s ON s.schema_id=t.schema_id
)
SELECT line FROM metadata_lines ORDER BY line;
