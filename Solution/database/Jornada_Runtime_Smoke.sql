SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Gate de execução SQL mínimo. Ele existe para capturar erros semânticos que
-- CREATE OR ALTER PROCEDURE pode aceitar por deferred name resolution e que
-- gates puramente textuais não detectam. Não persiste dados: toda a prova roda
-- sob transação e termina em ROLLBACK.
BEGIN TRY
    BEGIN TRANSACTION;

    -- O schema final da Jornada não pode depender dos warnings de chave larga do SQL Server.
    -- Chaves explícitas de índices rowstore clusterizados têm limite de 900 bytes;
    -- não clusterizados, 1700 bytes. INCLUDE/row locator implícito não entra na soma.
    -- Objetos internos do SQL Server ficam fora do contrato da aplicação.
    DECLARE @wide_index NVARCHAR(2048)=NULL;
    ;WITH index_width AS (
        SELECT
            i.object_id,
            i.index_id,
            i.name AS index_name,
            i.type,
            SUM(CASE WHEN c.max_length=-1 THEN 8001 ELSE c.max_length END) AS key_bytes,
            CASE WHEN i.type=1 THEN 900 ELSE 1700 END AS key_limit
        FROM sys.indexes i
        JOIN sys.objects o
          ON o.object_id=i.object_id
        JOIN sys.index_columns ic
          ON ic.object_id=i.object_id AND ic.index_id=i.index_id
        JOIN sys.columns c
          ON c.object_id=ic.object_id AND c.column_id=ic.column_id
        WHERE i.type IN(1,2)
          AND i.is_hypothetical=0
          AND o.is_ms_shipped=0
          AND ic.key_ordinal>0
        GROUP BY i.object_id,i.index_id,i.name,i.type
    )
    SELECT TOP(1) @wide_index=CONCAT(
        N'Índice ',QUOTENAME(OBJECT_SCHEMA_NAME(object_id)),N'.',QUOTENAME(OBJECT_NAME(object_id)),N'.',QUOTENAME(index_name),
        N' tem chave teórica de ',key_bytes,N' bytes; limite=',key_limit,N'.')
    FROM index_width
    WHERE key_bytes>key_limit
    ORDER BY key_bytes-key_limit DESC,index_name;

    IF @wide_index IS NOT NULL
        THROW 51987,@wide_index,1;

    -- Re-resolução de metadados dos módulos mais críticos do fluxo governado.
    EXEC sys.sp_refreshsqlmodule N'identidade.sp_recompor_gold_pessoa';
    EXEC sys.sp_refreshsqlmodule N'identidade.sp_abrir_caso_conflito_identidade';
    EXEC sys.sp_refreshsqlmodule N'identidade.sp_aplicar_caso_conflito_identidade';
    EXEC sys.sp_refreshsqlmodule N'identidade.sp_aplicar_correcao_identidade';
    EXEC sys.sp_refreshsqlmodule N'identidade.sp_sincronizar_atribuicao_fatos';
    EXEC sys.sp_refreshsqlmodule N'auditoria.sp_registrar_decisao_identidade';
    EXEC sys.sp_refreshsqlmodule N'auditoria.v_modelo_linkage_estado_evento';
    EXEC sys.sp_refreshsqlmodule N'auditoria.sp_calcular_fingerprint_modelo_linkage';
    EXEC sys.sp_refreshsqlmodule N'auditoria.sp_registrar_conferencia_linkage';
    EXEC sys.sp_refreshsqlmodule N'auditoria.sp_assert_conferencia_linkage_conforme';
    EXEC sys.sp_refreshsqlmodule N'auditoria.v_linkage_conferencia_evidencia';
    EXEC sys.sp_refreshsqlmodule N'ingestao.sp_recalcular_entrega';
    EXEC sys.sp_refreshsqlmodule N'ref.fn_telefone_br_canonico_v2';
    EXEC sys.sp_refreshsqlmodule N'ref.fn_email_canonico_v2';

    IF OBJECT_ID(N'auditoria.decisao_identidade_evento',N'U') IS NULL
       OR OBJECT_ID(N'auditoria.sp_registrar_decisao_identidade',N'P') IS NULL
       OR OBJECT_ID(N'auditoria.tr_decisao_identidade_evento_append_only',N'TR') IS NULL
        THROW 51986,'Ledger canônico de decisões de identidade ausente/incompleto.',1;

    IF OBJECT_ID(N'auditoria.modelo_linkage_estado_evento',N'U') IS NULL
       OR OBJECT_ID(N'auditoria.v_modelo_linkage_estado_evento',N'V') IS NULL
       OR OBJECT_ID(N'auditoria.tr_modelo_linkage_estado_evento_append_only',N'TR') IS NULL
       OR OBJECT_ID(N'identidade.tr_modelo_linkage_estado_evento',N'TR') IS NULL
        THROW 51987,'Ledger canônico de transições do modelo de Linkage ausente/incompleto.',1;

    IF OBJECT_ID(N'auditoria.linkage_conferencia_evidencia',N'U') IS NULL
       OR OBJECT_ID(N'auditoria.sp_calcular_fingerprint_modelo_linkage',N'P') IS NULL
       OR OBJECT_ID(N'auditoria.sp_registrar_conferencia_linkage',N'P') IS NULL
       OR OBJECT_ID(N'auditoria.sp_assert_conferencia_linkage_conforme',N'P') IS NULL
       OR OBJECT_ID(N'auditoria.tr_linkage_conferencia_evidencia_append_only',N'TR') IS NULL
        THROW 51985,'Contrato persistente da conferência independente de Linkage ausente/incompleto.',1;

    IF CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.BaseNormativa'))<>N'3.62'
       OR CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema'))<>N'3.70'
        THROW 51988,'Marcador persistente do schema não corresponde a Base 3.62 / Solution 3.70.',1;

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
