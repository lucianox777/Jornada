-- Cutover operacional do initial_uuid no Processor (SQL Server).
-- Pré-requisito: Jornada_Identidade_Progressiva.sql aplicado.
-- Não ativa Linkage probabilístico e não altera a atribuição canônica do vínculo.
SET XACT_ABORT ON;
GO
IF OBJECT_ID('identidade.pessoa_origem_progressiva','U') IS NULL
   OR OBJECT_ID('identidade.pessoa_origem_progressiva_evento','U') IS NULL
   OR OBJECT_ID('identidade.sp_assegurar_origem_progressiva','P') IS NULL
    THROW 51130,'Persistência progressiva V1 não instalada; cutover recusado.',1;
GO
BEGIN TRANSACTION;

-- Fecha o universo existente antes de ativar o gatilho para novas observações.
-- O procedimento é idempotente e mantém eventual UUID inicial já persistido.
DECLARE @pessoa_origem_id BIGINT;
DECLARE @resultado TABLE(
    initial_uuid UNIQUEIDENTIFIER NOT NULL,
    legacy_pessoa_uuid UNIQUEIDENTIFIER NULL,
    estado VARCHAR(20) NOT NULL,
    versao BIGINT NOT NULL
);
DECLARE progressiva_backfill CURSOR LOCAL FAST_FORWARD FOR
    SELECT o.pessoa_origem_id
      FROM silver.pessoa_origem o
      LEFT JOIN identidade.pessoa_origem_progressiva p
        ON p.pessoa_origem_id=o.pessoa_origem_id
     WHERE p.pessoa_origem_id IS NULL
     ORDER BY o.pessoa_origem_id;
OPEN progressiva_backfill;
FETCH NEXT FROM progressiva_backfill INTO @pessoa_origem_id;
WHILE @@FETCH_STATUS=0
BEGIN
    DELETE FROM @resultado;
    INSERT INTO @resultado(initial_uuid,legacy_pessoa_uuid,estado,versao)
        EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@pessoa_origem_id;
    FETCH NEXT FROM progressiva_backfill INTO @pessoa_origem_id;
END;
CLOSE progressiva_backfill;
DEALLOCATE progressiva_backfill;
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
END;
GO

IF EXISTS(
    SELECT 1
      FROM silver.pessoa_origem o
      LEFT JOIN identidade.pessoa_origem_progressiva p
        ON p.pessoa_origem_id=o.pessoa_origem_id
     WHERE p.pessoa_origem_id IS NULL)
BEGIN
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW 51131,'Cutover recusado: existem origens Silver sem initial_uuid.',1;
END;

COMMIT TRANSACTION;
GO
