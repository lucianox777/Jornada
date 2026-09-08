-- Cutover operacional do initial_uuid e da REFERENCIA determinística no Processor (SQL Server).
-- Pré-requisito: Jornada_Identidade_Progressiva.sql aplicado e backfill paginado concluído.
-- Não ativa Linkage probabilístico e não altera a atribuição canônica do vínculo.
SET XACT_ABORT ON;
GO
IF OBJECT_ID('identidade.pessoa_origem_progressiva','U') IS NULL
   OR OBJECT_ID('identidade.pessoa_origem_progressiva_evento','U') IS NULL
   OR OBJECT_ID('identidade.sp_assegurar_origem_progressiva','P') IS NULL
   OR OBJECT_ID('identidade.sp_publicar_referencia_progressiva_deterministica','P') IS NULL
    THROW 51130,'Persistência progressiva V1 não instalada; cutover recusado.',1;
GO

-- O backfill histórico é deliberadamente paginado/retomável pelo ProgressiveIdentityOriginStore.
-- Não varremos toda a população dentro desta migração: isso criaria uma transação longa e
-- imprópria para uma base municipal. O cutover só pode avançar quando o universo histórico
-- estiver completamente coberto.
IF EXISTS(
    SELECT 1
      FROM silver.pessoa_origem o
      LEFT JOIN identidade.pessoa_origem_progressiva p
        ON p.pessoa_origem_id=o.pessoa_origem_id
     WHERE p.pessoa_origem_id IS NULL)
    THROW 51131,'Cutover recusado: conclua o backfill paginado de initial_uuid antes de ativar o Processor.',1;
GO

-- O Processor insere vinculo_fonte sem OUTPUT. Esse é o primeiro ponto seguro, dentro
-- da mesma transação serializável, após a observação Silver existir. Evitamos trigger
-- em silver.pessoa_origem porque o INSERT atual usa OUTPUT INSERTED e SQL Server não
-- permite esse formato quando o alvo possui trigger habilitado.
CREATE OR ALTER TRIGGER identidade.tr_vinculo_fonte_progressiva
ON identidade.vinculo_fonte
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @pessoa_origem_id BIGINT;
    DECLARE @canonical_uuid UNIQUEIDENTIFIER;
    DECLARE @versao_resultado BIGINT;
    DECLARE @resultado TABLE(
        initial_uuid UNIQUEIDENTIFIER NOT NULL,
        legacy_pessoa_uuid UNIQUEIDENTIFIER NULL,
        estado VARCHAR(20) NOT NULL,
        versao BIGINT NOT NULL
    );

    DECLARE progressiva_novos CURSOR LOCAL FAST_FORWARD FOR
        SELECT DISTINCT o.pessoa_origem_id
          FROM inserted i
          JOIN silver.pessoa_observacao o
            ON o.pessoa_observacao_id=i.pessoa_observacao_id
          LEFT JOIN identidade.pessoa_origem_progressiva p
            ON p.pessoa_origem_id=o.pessoa_origem_id
         WHERE p.pessoa_origem_id IS NULL;

    OPEN progressiva_novos;
    FETCH NEXT FROM progressiva_novos INTO @pessoa_origem_id;
    WHILE @@FETCH_STATUS=0
    BEGIN
        DELETE FROM @resultado;
        INSERT INTO @resultado(initial_uuid,legacy_pessoa_uuid,estado,versao)
            EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@pessoa_origem_id;
        FETCH NEXT FROM progressiva_novos INTO @pessoa_origem_id;
    END;
    CLOSE progressiva_novos;
    DEALLOCATE progressiva_novos;

    -- Um mesmo lote não pode publicar dois UUIDs determinísticos para a mesma origem.
    IF EXISTS(
        SELECT o.pessoa_origem_id
          FROM inserted i
          JOIN silver.pessoa_observacao o ON o.pessoa_observacao_id=i.pessoa_observacao_id
         WHERE i.metodo_resolucao='CPF_DETERMINISTICO' AND i.status='RESOLVIDO' AND i.pessoa_uuid IS NOT NULL
         GROUP BY o.pessoa_origem_id
        HAVING COUNT(DISTINCT i.pessoa_uuid)>1)
        THROW 51132,'Vínculos CPF determinísticos divergentes para a mesma origem na mesma transação.',1;

    DECLARE progressiva_referencias CURSOR LOCAL FAST_FORWARD FOR
        SELECT DISTINCT o.pessoa_origem_id,i.pessoa_uuid
          FROM inserted i
          JOIN silver.pessoa_observacao o ON o.pessoa_observacao_id=i.pessoa_observacao_id
         WHERE i.metodo_resolucao='CPF_DETERMINISTICO' AND i.status='RESOLVIDO' AND i.pessoa_uuid IS NOT NULL;

    OPEN progressiva_referencias;
    FETCH NEXT FROM progressiva_referencias INTO @pessoa_origem_id,@canonical_uuid;
    WHILE @@FETCH_STATUS=0
    BEGIN
        SET @versao_resultado=NULL;
        EXEC identidade.sp_publicar_referencia_progressiva_deterministica
             @pessoa_origem_id=@pessoa_origem_id,
             @canonical_uuid=@canonical_uuid,
             @evidencia_referencia=N'CPF_ANCORA_DETERMINISTICA',
             @politica_versao=N'CPF_ANCORA_V1',
             @versao_resultado=@versao_resultado OUTPUT;
        FETCH NEXT FROM progressiva_referencias INTO @pessoa_origem_id,@canonical_uuid;
    END;
    CLOSE progressiva_referencias;
    DEALLOCATE progressiva_referencias;
END;
GO
