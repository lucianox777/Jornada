-- DT-23: contrato de persistência e ausência de automação de calibração.
IF OBJECT_ID(N'ref.dt23_politica_suficiencia', N'U') IS NULL
    THROW 52301, 'Tabela de política DT23 não instalada', 1;
IF OBJECT_ID(N'ref.dt23_diagnostico_suficiencia', N'U') IS NULL
    THROW 52302, 'Tabela de diagnóstico DT23 não instalada', 1;
IF OBJECT_ID(N'ref.vw_dt23_ultimo_diagnostico', N'V') IS NULL
    THROW 52305, 'Visão read-only do diagnóstico DT23 não instalada', 1;
-- Prova negativa: objetos DT-23 não podem instalar gatilhos de geração,
-- validação ou ativação de modelos. A consulta permanece somente leitura.
IF EXISTS (
    SELECT 1 FROM sys.triggers AS t
    WHERE t.parent_id IN (
        OBJECT_ID(N'ref.dt23_politica_suficiencia'),
        OBJECT_ID(N'ref.dt23_diagnostico_suficiencia'))
)
    THROW 52308, 'DT23 não pode possuir triggers nas tabelas observacionais', 1;
IF EXISTS (
    SELECT 1 FROM sys.sql_modules AS m
    WHERE m.object_id = OBJECT_ID(N'ref.vw_dt23_ultimo_diagnostico')
      AND (UPPER(m.definition) LIKE N'%GENERATE_DRAFT%'
        OR UPPER(m.definition) LIKE N'%ACTIVATE%'
        OR UPPER(m.definition) LIKE N'%VALIDATE%')
)
    THROW 52309, 'Visão DT23 não pode referenciar comandos de calibração', 1;
BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @politica UNIQUEIDENTIFIER = NEWID(), @diagnostico UNIQUEIDENTIFIER = NEWID();
    INSERT ref.dt23_politica_suficiencia (politica_id, versao, configuracao_json)
    VALUES (@politica, CONCAT('smoke-',CONVERT(VARCHAR(36),@politica)), N'{"minimumM":2,"minimumU":2}');
    INSERT ref.dt23_diagnostico_suficiencia
        (diagnostico_id, politica_id, data_corte, estado, contadores_json, justificativas_json, proveniencia_json)
    VALUES
        (@diagnostico, @politica, SYSUTCDATETIME(), 'INDETERMINADO',
         N'{"m":0,"u":0}', N'["sem verdade independente"]', N'{"fonte":"smoke"}');
    IF NOT EXISTS (SELECT 1 FROM ref.dt23_diagnostico_suficiencia
                   WHERE diagnostico_id=@diagnostico AND estado='INDETERMINADO')
        THROW 52303, 'Snapshot não persistido', 1;
    IF NOT EXISTS (SELECT 1 FROM ref.dt23_politica_suficiencia
                   WHERE politica_id=@politica AND ISJSON(configuracao_json)=1)
        THROW 52304, 'Política versionada não persistida', 1;
    IF NOT EXISTS (SELECT 1 FROM ref.vw_dt23_ultimo_diagnostico
                   WHERE diagnostico_id=@diagnostico AND politica_id=@politica
                     AND estado='INDETERMINADO')
        THROW 52306, 'Visão não reflete snapshot versionado', 1;
    DECLARE @novo UNIQUEIDENTIFIER = NEWID();
    INSERT ref.dt23_diagnostico_suficiencia
        (diagnostico_id, politica_id, data_corte, estado, contadores_json, justificativas_json, proveniencia_json, registrado_em)
    VALUES (@novo, @politica, DATEADD(SECOND,1,SYSUTCDATETIME()), 'AMARELO', N'{"m":1,"u":0}', N'["suporte insuficiente"]', N'{"fonte":"smoke"}', DATEADD(SECOND,1,SYSUTCDATETIME()));
    IF (SELECT COUNT(*) FROM ref.vw_dt23_ultimo_diagnostico WHERE politica_id=@politica) <> 1
       OR NOT EXISTS (SELECT 1 FROM ref.vw_dt23_ultimo_diagnostico
                      WHERE diagnostico_id=@novo AND estado='AMARELO')
        THROW 52307, 'Visão não escolhe o diagnóstico mais recente da política', 1;
    -- Verifica a restrição sem provocar erro 2627 dentro da transação:
    -- em sessões XACT_ABORT ON a violação invalidaria a transação inteira.
    DECLARE @corte DATETIME2(3) = (SELECT data_corte FROM ref.dt23_diagnostico_suficiencia WHERE diagnostico_id=@novo);
    IF NOT EXISTS (
        SELECT 1 FROM sys.key_constraints AS k
        WHERE k.parent_object_id=OBJECT_ID(N'ref.dt23_diagnostico_suficiencia')
          AND k.name=N'UQ_dt23_diagnostico_politica_corte'
          AND k.type='UQ'
    )
        THROW 52310, 'Unicidade por politica e corte ausente', 1;
    -- Backfill inserido depois não pode substituir um corte mais novo.
    DECLARE @backfill UNIQUEIDENTIFIER = NEWID();
    INSERT ref.dt23_diagnostico_suficiencia
        (diagnostico_id, politica_id, data_corte, estado, contadores_json, justificativas_json, proveniencia_json, registrado_em)
    VALUES (@backfill, @politica, DATEADD(DAY,-1,@corte), 'VERMELHO', N'{"m":0}', N'["backfill"]', N'{"fonte":"smoke"}', DATEADD(DAY,1,SYSUTCDATETIME()));
    IF NOT EXISTS (SELECT 1 FROM ref.vw_dt23_ultimo_diagnostico
                   WHERE politica_id=@politica AND diagnostico_id=@novo AND estado='AMARELO')
        THROW 52311, 'Backfill antigo substituiu diagnostico de corte mais recente', 1;
    -- Isolamento: uma política sem evidência não herda o diagnóstico de outra.
    DECLARE @politica_sem_snapshot UNIQUEIDENTIFIER = NEWID();
    INSERT ref.dt23_politica_suficiencia (politica_id, versao, configuracao_json)
    VALUES (@politica_sem_snapshot, CONCAT('smoke-empty-',CONVERT(VARCHAR(36),@politica_sem_snapshot)), N'{"minimumM":2,"minimumU":2}');
    IF EXISTS (SELECT 1 FROM ref.vw_dt23_ultimo_diagnostico WHERE politica_id=@politica_sem_snapshot)
        THROW 52312, 'Politica sem evidencias recebeu diagnostico indevido', 1;
    DECLARE @outro_diagnostico UNIQUEIDENTIFIER = NEWID();
    INSERT ref.dt23_diagnostico_suficiencia
        (diagnostico_id, politica_id, data_corte, estado, contadores_json, justificativas_json, proveniencia_json)
    VALUES (@outro_diagnostico, @politica_sem_snapshot, DATEADD(DAY,-10,@corte), 'VERMELHO',
            N'{"m":0,"u":0}', N'["sem amostra"]', N'{"fonte":"smoke"}');
    IF (SELECT COUNT(*) FROM ref.vw_dt23_ultimo_diagnostico
        WHERE politica_id IN (@politica,@politica_sem_snapshot)) <> 2
       OR NOT EXISTS (SELECT 1 FROM ref.vw_dt23_ultimo_diagnostico
                      WHERE politica_id=@politica_sem_snapshot AND diagnostico_id=@outro_diagnostico AND estado='VERMELHO')
       OR NOT EXISTS (SELECT 1 FROM ref.vw_dt23_ultimo_diagnostico
                      WHERE politica_id=@politica AND diagnostico_id=@novo AND estado='AMARELO')
        THROW 52313, 'Diagnosticos de politicas distintas foram misturados', 1;
    -- A versão de política é parte da identidade de auditoria retornada pela visão.
    IF NOT EXISTS (
        SELECT 1 FROM ref.vw_dt23_ultimo_diagnostico AS v
        INNER JOIN ref.dt23_politica_suficiencia AS p ON p.politica_id = v.politica_id
        WHERE v.politica_id = @politica AND v.diagnostico_id = @novo
          AND v.politica_versao = p.versao
          AND v.data_corte = @corte
          AND ISJSON(v.contadores_json) = 1
          AND ISJSON(v.justificativas_json) = 1
          AND ISJSON(v.proveniencia_json) = 1
    ) THROW 52314, 'Visao nao preserva versao, corte ou JSON auditavel', 1;
    -- Não é permitido alterar o estado de uma política por herança de outra.
    IF EXISTS (
        SELECT 1 FROM ref.vw_dt23_ultimo_diagnostico
        WHERE politica_id = @politica_sem_snapshot AND estado = 'AMARELO'
    ) THROW 52315, 'Diagnostico de politica alheia contaminou a visao', 1;
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
