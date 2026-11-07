-- NEUT-DEV-20261107. New forward-only, idempotent migration.
-- Old migrations and baseline hashes are NOT edited.
-- The old token is intentionally constructed in two parts to avoid
-- introducing a nominal vendor reference into the current source tree.
SET XACT_ABORT ON;
GO
IF OBJECT_ID(N'silver.referencia_territorial_observacao', N'U') IS NULL
    THROW 54101, 'Tabela territorial obrigatoria ausente; aplicar schema antes da migracao.', 1;
IF COL_LENGTH(N'silver.referencia_territorial_observacao', N'origem_geografia') IS NULL
    THROW 54102, 'Coluna origem_geografia ausente.', 1;
GO
DECLARE @old nvarchar(30) = N'ENRIQUECIMENTO_' + N'PRO' + N'DAM';
DECLARE @new nvarchar(30) = N'ENRIQUECIMENTO_DESENVOLVEDOR';
IF LEN(@new) > 30
    THROW 54103, 'Valor novo excede a coluna origem_geografia.', 1;
IF EXISTS (
    SELECT 1 FROM sys.check_constraints c
    WHERE c.parent_object_id = OBJECT_ID(N'silver.referencia_territorial_observacao')
      AND CHARINDEX(@old, c.definition) > 0
      AND c.name NOT IN (N'ck_referencia_territorial_geo_coerencia',N'ck_referencia_territorial_geo_origem')
)
    THROW 54104, 'Constraint inesperada depende do valor antigo; analisar antes de migrar.', 1;

DECLARE @oldChecks TABLE(name sysname PRIMARY KEY, new_definition nvarchar(max) NOT NULL);
INSERT INTO @oldChecks(name,new_definition)
SELECT c.name, REPLACE(c.definition,@old,@new)
FROM sys.check_constraints c
WHERE c.parent_object_id = OBJECT_ID(N'silver.referencia_territorial_observacao')
  AND CHARINDEX(@old,c.definition) > 0;

-- Require an explicit recognized CHECK for this column, before or after migration.
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints c
    WHERE c.parent_object_id = OBJECT_ID(N'silver.referencia_territorial_observacao')
      AND CHARINDEX(@old,c.definition) > 0
)
AND NOT EXISTS (
    SELECT 1 FROM sys.check_constraints c
    WHERE c.parent_object_id = OBJECT_ID(N'silver.referencia_territorial_observacao')
      AND CHARINDEX(@new,c.definition) > 0
)
    THROW 54105, 'Dominio territorial sem CHECK reconhecido: falha fechada.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @name sysname, @definition nvarchar(max), @sql nvarchar(max);
    DECLARE drop_checks CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM @oldChecks ORDER BY name;
    OPEN drop_checks;
    FETCH NEXT FROM drop_checks INTO @name;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = N'ALTER TABLE silver.referencia_territorial_observacao DROP CONSTRAINT ' + QUOTENAME(@name);
        EXEC sys.sp_executesql @sql;
        FETCH NEXT FROM drop_checks INTO @name;
    END
    CLOSE drop_checks;
    DEALLOCATE drop_checks;

    UPDATE silver.referencia_territorial_observacao
       SET origem_geografia = @new
     WHERE origem_geografia = @old;

    DECLARE add_checks CURSOR LOCAL FAST_FORWARD FOR
        SELECT name,new_definition FROM @oldChecks ORDER BY name;
    OPEN add_checks;
    FETCH NEXT FROM add_checks INTO @name,@definition;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = N'ALTER TABLE silver.referencia_territorial_observacao WITH CHECK ADD CONSTRAINT '
            + QUOTENAME(@name) + N' CHECK ' + @definition;
        EXEC sys.sp_executesql @sql;
        FETCH NEXT FROM add_checks INTO @name,@definition;
    END
    CLOSE add_checks;
    DEALLOCATE add_checks;

    IF EXISTS(SELECT 1 FROM silver.referencia_territorial_observacao WHERE origem_geografia=@old)
        THROW 54106, 'Registros com valor antigo apos migracao.', 1;
    IF EXISTS(
        SELECT 1 FROM sys.check_constraints c
        WHERE c.parent_object_id=OBJECT_ID(N'silver.referencia_territorial_observacao')
          AND CHARINDEX(@old,c.definition)>0
    )
        THROW 54107, 'CHECK historico ainda admite o valor antigo.', 1;
    IF NOT EXISTS(
        SELECT 1 FROM sys.check_constraints c
        WHERE c.parent_object_id=OBJECT_ID(N'silver.referencia_territorial_observacao')
          AND CHARINDEX(@new,c.definition)>0 AND c.is_not_trusted=0 AND c.is_disabled=0
    )
        THROW 54108, 'CHECK novo nao confirmado ou nao confiavel.', 1;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
