SET NOCOUNT ON;

WITH metadata_lines AS (
    SELECT CONCAT('COLUMN|',s.name,'|',t.name,'|',c.column_id,'|',c.name,'|',ty.name,'|',c.max_length,'|',c.precision,'|',c.scale,'|',c.is_nullable,'|',c.is_identity,'|',COALESCE(dc.definition,'')) AS line
    FROM sys.tables t
    JOIN sys.schemas s ON s.schema_id=t.schema_id
    JOIN sys.columns c ON c.object_id=t.object_id
    JOIN sys.types ty ON ty.user_type_id=c.user_type_id
    LEFT JOIN sys.default_constraints dc ON dc.parent_object_id=c.object_id AND dc.parent_column_id=c.column_id
    WHERE s.name NOT IN('sys','INFORMATION_SCHEMA')
    UNION ALL
    SELECT CONCAT('INDEX|',s.name,'|',t.name,'|',i.name,'|',i.is_unique,'|',i.type_desc,'|',ic.key_ordinal,'|',ic.is_descending_key,'|',ic.is_included_column,'|',c.name,'|',COALESCE(i.filter_definition,''))
    FROM sys.tables t
    JOIN sys.schemas s ON s.schema_id=t.schema_id
    JOIN sys.indexes i ON i.object_id=t.object_id AND i.index_id>0 AND i.is_hypothetical=0
    JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE s.name NOT IN('sys','INFORMATION_SCHEMA')
    UNION ALL
    SELECT CONCAT('OBJECT|',s.name,'|',o.name,'|',o.type,'|',CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),COALESCE(m.definition,''))),2))
    FROM sys.objects o
    JOIN sys.schemas s ON s.schema_id=o.schema_id
    LEFT JOIN sys.sql_modules m ON m.object_id=o.object_id
    WHERE o.is_ms_shipped=0 AND o.type IN('V','P','TR','FN','IF','TF')
    UNION ALL
    SELECT CONCAT('CHECK|',s.name,'|',t.name,'|',cc.name,'|',cc.is_disabled,'|',cc.is_not_trusted,'|',cc.definition)
    FROM sys.check_constraints cc
    JOIN sys.tables t ON t.object_id=cc.parent_object_id
    JOIN sys.schemas s ON s.schema_id=t.schema_id
    UNION ALL
    SELECT CONCAT('FK|',s.name,'|',t.name,'|',fk.name,'|',fk.is_disabled,'|',fk.is_not_trusted,'|',OBJECT_SCHEMA_NAME(fk.referenced_object_id),'|',OBJECT_NAME(fk.referenced_object_id))
    FROM sys.foreign_keys fk
    JOIN sys.tables t ON t.object_id=fk.parent_object_id
    JOIN sys.schemas s ON s.schema_id=t.schema_id
)
SELECT line FROM metadata_lines ORDER BY line;
