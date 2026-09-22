SET NOCOUNT ON;
SET XACT_ABORT ON;

/*
  Limpeza controlada para SYNTHETIC_CALIBRATION_DEV.
  Preserva:
    - todo o schema ref (incluindo frequências IBGE ativas);
    - Jornada.EnvironmentProfile e demais extended properties;
    - auditoria.linkage_avaliacao_sintetica + métricas;
    - schema/migration ledger e definição física;
    - catálogos estáticos de implementação QC/possibilidade (não são dados de carga).

  Remove somente estado operacional materializado nos schemas abaixo, incluindo\n  indicadores de qualidade associados a Pessoa.
  Fail-closed se uma tabela preservada ainda possuir FK habilitada para estado
  que seria apagado.
*/

IF ISNULL(CONVERT(NVARCHAR(32),(
       SELECT value FROM sys.extended_properties
       WHERE class=0 AND name=N'Jornada.EnvironmentProfile')),N'')<>N'Development'
    THROW 51930,'Limpeza sintética só é permitida com Jornada.EnvironmentProfile=Development.',1;

IF OBJECT_ID(N'auditoria.linkage_avaliacao_sintetica',N'U') IS NULL
   OR OBJECT_ID(N'auditoria.linkage_avaliacao_sintetica_metrica',N'U') IS NULL
    THROW 51931,'Ledger de avaliação sintética não está instalado.',1;

DECLARE @avaliacoes_before BIGINT=(SELECT COUNT_BIG(*) FROM auditoria.linkage_avaliacao_sintetica);
DECLARE @metricas_before BIGINT=(SELECT COUNT_BIG(*) FROM auditoria.linkage_avaliacao_sintetica_metrica);
DECLARE @ref_versoes_before BIGINT=(SELECT COUNT_BIG(*) FROM ref.frequencia_nome_versao);
DECLARE @ref_linhas_before BIGINT=(SELECT COUNT_BIG(*) FROM ref.frequencia_nome);

DECLARE @mutable TABLE(
    object_id INT NOT NULL PRIMARY KEY,
    full_name NVARCHAR(517) NOT NULL
);

INSERT @mutable(object_id,full_name)
SELECT t.object_id,QUOTENAME(s.name)+N'.'+QUOTENAME(t.name)
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE s.name IN(N'ingestao',N'bronze',N'silver',N'gold',N'identidade',N'qualidade',N'auditoria')
  AND NOT(
      s.name=N'auditoria'
      AND t.name IN(N'linkage_avaliacao_sintetica',N'linkage_avaliacao_sintetica_metrica'))
  AND NOT(
      s.name=N'qualidade'
      AND t.name IN(N'qc_registro_implementacao',N'possibilidade_implementacao'));

IF EXISTS(
    SELECT 1
    FROM sys.foreign_keys fk
    JOIN @mutable parent_table ON parent_table.object_id=fk.referenced_object_id
    LEFT JOIN @mutable child_table ON child_table.object_id=fk.parent_object_id
    WHERE child_table.object_id IS NULL
      AND fk.is_disabled=0)
BEGIN
    DECLARE @external NVARCHAR(2048)=(
        SELECT TOP(1)
          CONCAT(
            QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)),N'.',
            QUOTENAME(OBJECT_NAME(fk.parent_object_id)),N' -> ',
            parent_table.full_name,N' (',QUOTENAME(fk.name),N')')
        FROM sys.foreign_keys fk
        JOIN @mutable parent_table ON parent_table.object_id=fk.referenced_object_id
        LEFT JOIN @mutable child_table ON child_table.object_id=fk.parent_object_id
        WHERE child_table.object_id IS NULL
          AND fk.is_disabled=0
        ORDER BY fk.name);
    THROW 51932,@external,1;
END;

DECLARE @enabled_constraints TABLE(
    full_name NVARCHAR(517) NOT NULL,
    constraint_name SYSNAME NOT NULL,
    PRIMARY KEY(full_name,constraint_name)
);
INSERT @enabled_constraints(full_name,constraint_name)
SELECT m.full_name,o.name
FROM @mutable m
JOIN (
    SELECT parent_object_id,name,is_disabled FROM sys.foreign_keys
    UNION ALL
    SELECT parent_object_id,name,is_disabled FROM sys.check_constraints
) o ON o.parent_object_id=m.object_id
WHERE o.is_disabled=0;

DECLARE @enabled_triggers TABLE(
    full_name NVARCHAR(517) NOT NULL,
    trigger_name SYSNAME NOT NULL,
    PRIMARY KEY(full_name,trigger_name)
);
INSERT @enabled_triggers(full_name,trigger_name)
SELECT m.full_name,tr.name
FROM @mutable m
JOIN sys.triggers tr ON tr.parent_id=m.object_id
WHERE tr.is_disabled=0;

BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @table NVARCHAR(517),@name SYSNAME,@sql NVARCHAR(MAX);

    DECLARE constraint_disable CURSOR LOCAL FAST_FORWARD FOR
        SELECT full_name,constraint_name FROM @enabled_constraints ORDER BY full_name,constraint_name;
    OPEN constraint_disable;
    FETCH NEXT FROM constraint_disable INTO @table,@name;
    WHILE @@FETCH_STATUS=0
    BEGIN
        SET @sql=N'ALTER TABLE '+@table+N' NOCHECK CONSTRAINT '+QUOTENAME(@name)+N';';
        EXEC sys.sp_executesql @sql;
        FETCH NEXT FROM constraint_disable INTO @table,@name;
    END;
    CLOSE constraint_disable;
    DEALLOCATE constraint_disable;

    DECLARE trigger_disable CURSOR LOCAL FAST_FORWARD FOR
        SELECT full_name,trigger_name FROM @enabled_triggers ORDER BY full_name,trigger_name;
    OPEN trigger_disable;
    FETCH NEXT FROM trigger_disable INTO @table,@name;
    WHILE @@FETCH_STATUS=0
    BEGIN
        SET @sql=N'DISABLE TRIGGER '+QUOTENAME(@name)+N' ON '+@table+N';';
        EXEC sys.sp_executesql @sql;
        FETCH NEXT FROM trigger_disable INTO @table,@name;
    END;
    CLOSE trigger_disable;
    DEALLOCATE trigger_disable;

    DECLARE data_delete CURSOR LOCAL FAST_FORWARD FOR
        SELECT full_name FROM @mutable ORDER BY full_name;
    OPEN data_delete;
    FETCH NEXT FROM data_delete INTO @table;
    WHILE @@FETCH_STATUS=0
    BEGIN
        SET @sql=N'DELETE FROM '+@table+N';';
        EXEC sys.sp_executesql @sql;
        FETCH NEXT FROM data_delete INTO @table;
    END;
    CLOSE data_delete;
    DEALLOCATE data_delete;

    DECLARE constraint_enable CURSOR LOCAL FAST_FORWARD FOR
        SELECT full_name,constraint_name FROM @enabled_constraints ORDER BY full_name,constraint_name;
    OPEN constraint_enable;
    FETCH NEXT FROM constraint_enable INTO @table,@name;
    WHILE @@FETCH_STATUS=0
    BEGIN
        SET @sql=N'ALTER TABLE '+@table+N' WITH CHECK CHECK CONSTRAINT '+QUOTENAME(@name)+N';';
        EXEC sys.sp_executesql @sql;
        FETCH NEXT FROM constraint_enable INTO @table,@name;
    END;
    CLOSE constraint_enable;
    DEALLOCATE constraint_enable;

    DECLARE trigger_enable CURSOR LOCAL FAST_FORWARD FOR
        SELECT full_name,trigger_name FROM @enabled_triggers ORDER BY full_name,trigger_name;
    OPEN trigger_enable;
    FETCH NEXT FROM trigger_enable INTO @table,@name;
    WHILE @@FETCH_STATUS=0
    BEGIN
        SET @sql=N'ENABLE TRIGGER '+QUOTENAME(@name)+N' ON '+@table+N';';
        EXEC sys.sp_executesql @sql;
        FETCH NEXT FROM trigger_enable INTO @table,@name;
    END;
    CLOSE trigger_enable;
    DEALLOCATE trigger_enable;

    IF (SELECT COUNT_BIG(*) FROM auditoria.linkage_avaliacao_sintetica)<>@avaliacoes_before
       OR (SELECT COUNT_BIG(*) FROM auditoria.linkage_avaliacao_sintetica_metrica)<>@metricas_before
        THROW 51933,'Limpeza alterou o ledger sintético preservado.',1;

    IF (SELECT COUNT_BIG(*) FROM ref.frequencia_nome_versao)<>@ref_versoes_before
       OR (SELECT COUNT_BIG(*) FROM ref.frequencia_nome)<>@ref_linhas_before
        THROW 51934,'Limpeza alterou a referência nominal preservada.',1;

    IF ISNULL(CONVERT(NVARCHAR(32),(
           SELECT value FROM sys.extended_properties
           WHERE class=0 AND name=N'Jornada.EnvironmentProfile')),N'')<>N'Development'
        THROW 51935,'Limpeza alterou o marcador residente de Development.',1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF CURSOR_STATUS('local','constraint_disable')>=-1
    BEGIN
        BEGIN TRY CLOSE constraint_disable; END TRY BEGIN CATCH END CATCH;
        BEGIN TRY DEALLOCATE constraint_disable; END TRY BEGIN CATCH END CATCH;
    END;
    IF CURSOR_STATUS('local','trigger_disable')>=-1
    BEGIN
        BEGIN TRY CLOSE trigger_disable; END TRY BEGIN CATCH END CATCH;
        BEGIN TRY DEALLOCATE trigger_disable; END TRY BEGIN CATCH END CATCH;
    END;
    IF CURSOR_STATUS('local','data_delete')>=-1
    BEGIN
        BEGIN TRY CLOSE data_delete; END TRY BEGIN CATCH END CATCH;
        BEGIN TRY DEALLOCATE data_delete; END TRY BEGIN CATCH END CATCH;
    END;
    IF CURSOR_STATUS('local','constraint_enable')>=-1
    BEGIN
        BEGIN TRY CLOSE constraint_enable; END TRY BEGIN CATCH END CATCH;
        BEGIN TRY DEALLOCATE constraint_enable; END TRY BEGIN CATCH END CATCH;
    END;
    IF CURSOR_STATUS('local','trigger_enable')>=-1
    BEGIN
        BEGIN TRY CLOSE trigger_enable; END TRY BEGIN CATCH END CATCH;
        BEGIN TRY DEALLOCATE trigger_enable; END TRY BEGIN CATCH END CATCH;
    END;
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT
    @avaliacoes_before AS synthetic_evaluations_preserved,
    @metricas_before AS synthetic_metrics_preserved,
    @ref_versoes_before AS name_reference_versions_preserved,
    @ref_linhas_before AS name_reference_rows_preserved;
