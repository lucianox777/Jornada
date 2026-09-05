SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Gate de execução SQL mínimo. Ele existe para capturar erros semânticos que
-- CREATE OR ALTER PROCEDURE pode aceitar por deferred name resolution e que
-- gates puramente textuais não detectam. Não persiste dados: toda a prova roda
-- sob transação e termina em ROLLBACK.
BEGIN TRY
    BEGIN TRANSACTION;

    -- Re-resolução de metadados dos módulos mais críticos do fluxo governado.
    EXEC sys.sp_refreshsqlmodule N'identidade.sp_recompor_gold_pessoa';
    EXEC sys.sp_refreshsqlmodule N'identidade.sp_abrir_caso_conflito_identidade';
    EXEC sys.sp_refreshsqlmodule N'identidade.sp_aplicar_caso_conflito_identidade';
    EXEC sys.sp_refreshsqlmodule N'identidade.sp_aplicar_correcao_identidade';
    EXEC sys.sp_refreshsqlmodule N'identidade.sp_sincronizar_atribuicao_fatos';
    EXEC sys.sp_refreshsqlmodule N'ingestao.sp_recalcular_entrega';
    EXEC sys.sp_refreshsqlmodule N'ref.fn_telefone_br_canonico_v2';
    EXEC sys.sp_refreshsqlmodule N'ref.fn_email_canonico_v2';

    IF CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.BaseNormativa'))<>N'3.62'
       OR CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema'))<>N'3.69'
        THROW 51988,'Marcador persistente do schema não corresponde a Base 3.62 / Solution 3.69.',1;

    IF ref.fn_email_canonico_v2(N'JOSÉ@EXAMPLE.ORG')<>N'josÉ@example.org'
       OR ref.fn_email_canonico_v2(N'Jose'+NCHAR(769)+N'@Example.org')<>N'jose'+NCHAR(769)+N'@example.org'
        THROW 51989,'EMAIL_CANONICO_V2 divergiu da semântica determinística esperada.',1;

    -- Caminho com observações resolvidas: a recomposição deve manter/publicar Gold.
    DECLARE @existente UNIQUEIDENTIFIER='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1';
    IF NOT EXISTS(SELECT 1 FROM identidade.pessoa WHERE pessoa_uuid=@existente AND status='ATIVO')
        THROW 51990,'Fixture do runtime smoke não contém a Pessoa esperada.',1;

    EXEC identidade.sp_recompor_gold_pessoa @pessoa_uuid=@existente;
    IF NOT EXISTS(SELECT 1 FROM gold.pessoa WHERE pessoa_uuid=@existente)
        THROW 51991,'sp_recompor_gold_pessoa não publicou/manteve a Pessoa com observações resolvidas.',1;

    -- Caminho sem observações: exercita a decisão pós-MERGE. Foi exatamente aqui
    -- que a v3.65 referenciava o CTE obs fora de escopo e falhava em runtime.
    DECLARE @sem_observacao UNIQUEIDENTIFIER='f3670000-0000-4000-8000-000000000001';
    IF EXISTS(SELECT 1 FROM identidade.pessoa WHERE pessoa_uuid=@sem_observacao)
        DELETE FROM gold.pessoa WHERE pessoa_uuid=@sem_observacao;
    IF NOT EXISTS(SELECT 1 FROM identidade.pessoa WHERE pessoa_uuid=@sem_observacao)
        INSERT identidade.pessoa(pessoa_uuid,status) VALUES(@sem_observacao,'ATIVO');

    INSERT gold.pessoa(pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,atualizado_em)
    VALUES(@sem_observacao,NULL,'SEM_CPF',N'Fixture obsoleta','2000-01-01',N'Fixture',1,'BASELINE_FONTE_UNICA',SYSDATETIMEOFFSET());

    EXEC identidade.sp_recompor_gold_pessoa @pessoa_uuid=@sem_observacao;
    IF EXISTS(SELECT 1 FROM gold.pessoa WHERE pessoa_uuid=@sem_observacao)
        THROW 51992,'sp_recompor_gold_pessoa preservou Gold obsoleta para Pessoa sem fonte corrente.',1;

    ROLLBACK TRANSACTION;
    PRINT 'JORNADA SQL RUNTIME SMOKE: OK';
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
