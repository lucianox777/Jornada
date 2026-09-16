SET NOCOUNT ON;

-- Fingerprint estrutural: nomes físicos gerados automaticamente pelo SQL Server
-- (PK__..., FK__..., UQ__...) não fazem parte da semântica do schema e mudam entre
-- bancos criados a partir do mesmo DDL. Por isso índices, CHECKs e FKs são descritos
-- por estrutura/definição, não pelo nome do objeto de catálogo. Nomes de objetos
-- programáveis continuam incluídos porque procedures/views/functions são API do schema.
-- Todos os fragmentos textuais usam DATABASE_DEFAULT para reproduzir o resultado
-- também em baselines criadas com collations legadas.
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
        i.is_unique,N'|',i.is_primary_key,N'|',i.is_unique_constraint,N'|',
        i.type_desc COLLATE DATABASE_DEFAULT,N'|',ic.key_ordinal,N'|',ic.is_descending_key,N'|',ic.is_included_column,N'|',
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
        fk.is_disabled,N'|',fk.is_not_trusted,N'|',
        fk.delete_referential_action_desc COLLATE DATABASE_DEFAULT,N'|',
        fk.update_referential_action_desc COLLATE DATABASE_DEFAULT,N'|',
        pc.name COLLATE DATABASE_DEFAULT,N'|',
        rs.name COLLATE DATABASE_DEFAULT,N'|',
        rt.name COLLATE DATABASE_DEFAULT,N'|',
        rc.name COLLATE DATABASE_DEFAULT,N'|',
        fkc.constraint_column_id)
    FROM sys.foreign_keys fk
    JOIN sys.tables t ON t.object_id=fk.parent_object_id
    JOIN sys.schemas s ON s.schema_id=t.schema_id
    JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
    JOIN sys.columns pc ON pc.object_id=fkc.parent_object_id AND pc.column_id=fkc.parent_column_id
    JOIN sys.tables rt ON rt.object_id=fkc.referenced_object_id
    JOIN sys.schemas rs ON rs.schema_id=rt.schema_id
    JOIN sys.columns rc ON rc.object_id=fkc.referenced_object_id AND rc.column_id=fkc.referenced_column_id
)
SELECT line FROM metadata_lines ORDER BY line;
