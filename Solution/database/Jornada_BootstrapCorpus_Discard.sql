SET NOCOUNT ON;
SET XACT_ABORT ON;

/*
  Lifecycle do corpus sintético de bootstrap.

  O corpus SCALE-* existe somente para derivar/validar o modelo inicial. Depois que
  existe exatamente um modelo calibrado ATIVO, os dados de Pessoa usados no bootstrap
  deixam de fazer parte do estado operacional. O modelo, seus parâmetros, rulesets,
  referências IBGE e evidências de governança não são removidos.

  Segurança:
    - o chamador deve declarar explicitamente o perfil residente esperado;
    - a rotina falha se não houver exatamente um modelo calibrado ATIVO;
    - constraints/triggers são restaurados WITH CHECK;
    - qualquer referência indireta esquecida faz o commit falhar e reverte tudo.
*/
DECLARE @expectedProfile NVARCHAR(32)=N'$(EXPECTED_ENVIRONMENT_PROFILE)';
DECLARE @residentProfile NVARCHAR(32)=ISNULL(CONVERT(NVARCHAR(32),(
    SELECT value FROM sys.extended_properties
    WHERE class=0 AND name=N'Jornada.EnvironmentProfile')),N'');

IF @expectedProfile NOT IN(N'Development',N'Homologation',N'Production')
    THROW 51940,'Perfil esperado inválido para descarte do corpus de bootstrap.',1;
IF @residentProfile<>@expectedProfile
    THROW 51941,'Perfil residente diverge do perfil explicitamente autorizado para o descarte.',1;

DECLARE @lifecycle NVARCHAR(40)=ISNULL(CONVERT(NVARCHAR(40),(
    SELECT value FROM sys.extended_properties
    WHERE class=0 AND name=N'Jornada.BootstrapCorpusLifecycle')),N'');
IF @lifecycle<>N'PENDING_DISCARD'
    THROW 51947,'Corpus não está marcado explicitamente como PENDING_DISCARD; descarte recusado.',1;

DECLARE @activeModels BIGINT=(
    SELECT COUNT_BIG(*) FROM identidade.modelo_linkage
    WHERE status=N'ATIVO' AND ISNULL(amostra_metodo,N'')<>N'SEED_DEV_FIXO_NAO_TREINADO');
IF @activeModels<>1
    THROW 51942,'Descarte exige exatamente um modelo calibrado ATIVO.',1;

DECLARE @modelId UNIQUEIDENTIFIER=(
    SELECT modelo_id FROM identidade.modelo_linkage
    WHERE status=N'ATIVO' AND ISNULL(amostra_metodo,N'')<>N'SEED_DEV_FIXO_NAO_TREINADO');
DECLARE @modelVersion INT=(SELECT versao FROM identidade.modelo_linkage WHERE modelo_id=@modelId);
DECLARE @parameterCount BIGINT=(SELECT COUNT_BIG(*) FROM identidade.parametro_linkage WHERE modelo_id=@modelId);
DECLARE @rulesetCount BIGINT=(SELECT COUNT_BIG(*) FROM identidade.linkage_ruleset WHERE modelo_id=@modelId);

CREATE TABLE #origin(pessoa_origem_id BIGINT NOT NULL PRIMARY KEY);
INSERT #origin
SELECT pessoa_origem_id FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-%';

IF NOT EXISTS(SELECT 1 FROM #origin)
BEGIN
    IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.BootstrapCorpusLifecycle')
        EXEC sys.sp_updateextendedproperty @name=N'Jornada.BootstrapCorpusLifecycle',@value=N'DISCARDED';
    ELSE
        EXEC sys.sp_addextendedproperty @name=N'Jornada.BootstrapCorpusLifecycle',@value=N'DISCARDED';
    SELECT @residentProfile AS environment_profile,@modelId AS active_model_id,@modelVersion AS active_model_version,
           CAST(0 AS BIGINT) AS bootstrap_origins_removed,N'ALREADY_DISCARDED' AS lifecycle_status;
    RETURN;
END;

CREATE TABLE #observation(pessoa_observacao_id BIGINT NOT NULL PRIMARY KEY);
INSERT #observation
SELECT pessoa_observacao_id FROM silver.pessoa_observacao
WHERE pessoa_origem_id IN(SELECT pessoa_origem_id FROM #origin);

CREATE TABLE #person(pessoa_uuid UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
INSERT #person
SELECT DISTINCT vf.pessoa_uuid
FROM identidade.vinculo_fonte vf
JOIN #observation o ON o.pessoa_observacao_id=vf.pessoa_observacao_id
WHERE vf.pessoa_uuid IS NOT NULL
UNION
SELECT DISTINCT vc.pessoa_uuid
FROM identidade.v_vinculo_corrente vc
JOIN #observation o ON o.pessoa_observacao_id=vc.pessoa_observacao_id
WHERE vc.pessoa_uuid IS NOT NULL;

DECLARE @originCount BIGINT=(SELECT COUNT_BIG(*) FROM #origin);
DECLARE @observationCount BIGINT=(SELECT COUNT_BIG(*) FROM #observation);
DECLARE @personCount BIGINT=(SELECT COUNT_BIG(*) FROM #person);

DECLARE @mutable TABLE(object_id INT PRIMARY KEY,full_name NVARCHAR(517) NOT NULL);
INSERT @mutable
SELECT t.object_id,QUOTENAME(s.name)+N'.'+QUOTENAME(t.name)
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE s.name IN(N'ingestao',N'bronze',N'silver',N'gold',N'serving',N'identidade',N'qualidade',N'auditoria')
  AND t.name NOT IN(N'modelo_linkage',N'parametro_linkage',N'linkage_ruleset');

DECLARE @constraints TABLE(full_name NVARCHAR(517),constraint_name SYSNAME,PRIMARY KEY(full_name,constraint_name));
INSERT @constraints
SELECT m.full_name,o.name
FROM @mutable m
JOIN (
    SELECT parent_object_id,name,is_disabled FROM sys.foreign_keys
    UNION ALL SELECT parent_object_id,name,is_disabled FROM sys.check_constraints
) o ON o.parent_object_id=m.object_id
WHERE o.is_disabled=0;

DECLARE @triggers TABLE(full_name NVARCHAR(517),trigger_name SYSNAME,PRIMARY KEY(full_name,trigger_name));
INSERT @triggers
SELECT m.full_name,tr.name FROM @mutable m JOIN sys.triggers tr ON tr.parent_id=m.object_id WHERE tr.is_disabled=0;

BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @table NVARCHAR(517),@name SYSNAME,@sql NVARCHAR(MAX);

    DECLARE c_disable CURSOR LOCAL FAST_FORWARD FOR SELECT full_name,constraint_name FROM @constraints;
    OPEN c_disable; FETCH NEXT FROM c_disable INTO @table,@name;
    WHILE @@FETCH_STATUS=0 BEGIN
        EXEC(N'ALTER TABLE '+@table+N' NOCHECK CONSTRAINT '+QUOTENAME(@name)+N';');
        FETCH NEXT FROM c_disable INTO @table,@name;
    END
    CLOSE c_disable; DEALLOCATE c_disable;

    DECLARE t_disable CURSOR LOCAL FAST_FORWARD FOR SELECT full_name,trigger_name FROM @triggers;
    OPEN t_disable; FETCH NEXT FROM t_disable INTO @table,@name;
    WHILE @@FETCH_STATUS=0 BEGIN
        EXEC(N'DISABLE TRIGGER '+QUOTENAME(@name)+N' ON '+@table+N';');
        FETCH NEXT FROM t_disable INTO @table,@name;
    END
    CLOSE t_disable; DEALLOCATE t_disable;

    /* Remove folhas que carregam diretamente uma das identidades do corpus. */
    DECLARE data_delete CURSOR LOCAL FAST_FORWARD FOR
    SELECT m.full_name
    FROM @mutable m
    WHERE m.full_name NOT IN(N'[silver].[pessoa_observacao]',N'[silver].[pessoa_origem]')
      AND EXISTS(
          SELECT 1 FROM sys.columns c
          WHERE c.object_id=m.object_id
            AND c.name IN(N'pessoa_uuid',N'pessoa_observacao_id',N'pessoa_origem_id',N'pessoa_origem_id_publicado'))
    ORDER BY m.full_name;
    OPEN data_delete; FETCH NEXT FROM data_delete INTO @table;
    WHILE @@FETCH_STATUS=0 BEGIN
        DECLARE @predicate NVARCHAR(MAX)=N'';
        IF COL_LENGTH(REPLACE(REPLACE(@table,N'[',N''),N']',N''),N'pessoa_uuid') IS NOT NULL
            SET @predicate=@predicate+CASE WHEN LEN(@predicate)>0 THEN N' OR ' ELSE N'' END+N'pessoa_uuid IN (SELECT pessoa_uuid FROM #person)';
        IF COL_LENGTH(REPLACE(REPLACE(@table,N'[',N''),N']',N''),N'pessoa_observacao_id') IS NOT NULL
            SET @predicate=@predicate+CASE WHEN LEN(@predicate)>0 THEN N' OR ' ELSE N'' END+N'pessoa_observacao_id IN (SELECT pessoa_observacao_id FROM #observation)';
        IF COL_LENGTH(REPLACE(REPLACE(@table,N'[',N''),N']',N''),N'pessoa_origem_id') IS NOT NULL
            SET @predicate=@predicate+CASE WHEN LEN(@predicate)>0 THEN N' OR ' ELSE N'' END+N'pessoa_origem_id IN (SELECT pessoa_origem_id FROM #origin)';
        IF COL_LENGTH(REPLACE(REPLACE(@table,N'[',N''),N']',N''),N'pessoa_origem_id_publicado') IS NOT NULL
            SET @predicate=@predicate+CASE WHEN LEN(@predicate)>0 THEN N' OR ' ELSE N'' END+N'pessoa_origem_id_publicado IN (SELECT pessoa_origem_id FROM #origin)';
        IF LEN(@predicate)>0 EXEC(N'DELETE FROM '+@table+N' WHERE '+@predicate+N';');
        FETCH NEXT FROM data_delete INTO @table;
    END
    CLOSE data_delete; DEALLOCATE data_delete;

    DELETE FROM silver.pessoa_observacao WHERE pessoa_observacao_id IN(SELECT pessoa_observacao_id FROM #observation);
    DELETE FROM silver.pessoa_origem WHERE pessoa_origem_id IN(SELECT pessoa_origem_id FROM #origin);

    DECLARE c_enable CURSOR LOCAL FAST_FORWARD FOR SELECT full_name,constraint_name FROM @constraints;
    OPEN c_enable; FETCH NEXT FROM c_enable INTO @table,@name;
    WHILE @@FETCH_STATUS=0 BEGIN
        EXEC(N'ALTER TABLE '+@table+N' WITH CHECK CHECK CONSTRAINT '+QUOTENAME(@name)+N';');
        FETCH NEXT FROM c_enable INTO @table,@name;
    END
    CLOSE c_enable; DEALLOCATE c_enable;

    DECLARE t_enable CURSOR LOCAL FAST_FORWARD FOR SELECT full_name,trigger_name FROM @triggers;
    OPEN t_enable; FETCH NEXT FROM t_enable INTO @table,@name;
    WHILE @@FETCH_STATUS=0 BEGIN
        EXEC(N'ENABLE TRIGGER '+QUOTENAME(@name)+N' ON '+@table+N';');
        FETCH NEXT FROM t_enable INTO @table,@name;
    END
    CLOSE t_enable; DEALLOCATE t_enable;

    IF EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-%')
        THROW 51943,'Corpus SCALE permaneceu após o descarte.',1;
    IF NOT EXISTS(SELECT 1 FROM identidade.modelo_linkage WHERE modelo_id=@modelId AND versao=@modelVersion AND status=N'ATIVO')
        THROW 51944,'Modelo ativo foi alterado durante o descarte.',1;
    IF (SELECT COUNT_BIG(*) FROM identidade.parametro_linkage WHERE modelo_id=@modelId)<>@parameterCount
        THROW 51945,'Parâmetros do modelo foram alterados durante o descarte.',1;
    IF (SELECT COUNT_BIG(*) FROM identidade.linkage_ruleset WHERE modelo_id=@modelId)<>@rulesetCount
        THROW 51946,'Ruleset do modelo foi alterado durante o descarte.',1;

    IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.BootstrapCorpusLifecycle')
        EXEC sys.sp_updateextendedproperty @name=N'Jornada.BootstrapCorpusLifecycle',@value=N'DISCARDED';
    ELSE
        EXEC sys.sp_addextendedproperty @name=N'Jornada.BootstrapCorpusLifecycle',@value=N'DISCARDED';

    COMMIT TRANSACTION;

    SELECT @residentProfile AS environment_profile,@modelId AS active_model_id,@modelVersion AS active_model_version,
           @originCount AS bootstrap_origins_removed,@observationCount AS bootstrap_observations_removed,
           @personCount AS bootstrap_people_removed,N'DISCARDED' AS lifecycle_status;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
