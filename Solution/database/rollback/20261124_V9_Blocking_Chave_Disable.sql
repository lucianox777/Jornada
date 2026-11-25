-- RF #1936. Reversao conservadora: desabilita disponibilidade da projecao.
-- Preserva integralmente tabela/dados V9 e legados; nenhum DROP/DELETE/TRUNCATE.
-- Compartilha lock transacional com C07: espera produtores autorizados terminarem.
-- Tambem desabilitar opt-in nos hosts; retornar consumidores ao estado anterior
-- somente por rollback da versao da aplicacao, nunca fallback automatico V9.
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
DECLARE @lock_result int;
EXEC @lock_result=sys.sp_getapplock @Resource=N'Jornada.V9.BlockingSchema',
    @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
IF @lock_result<0 THROW 52960,'V9 blocking schema lock unavailable.',1;
IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.V9.BlockingSchemaEnabled')
    EXEC sys.sp_updateextendedproperty @name=N'Jornada.V9.BlockingSchemaEnabled',@value=N'0';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'Jornada.V9.BlockingSchemaEnabled',@value=N'0';
COMMIT TRANSACTION;
GO
