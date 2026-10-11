-- DT-23: contrato de persistência e ausência de automação de calibração.
IF OBJECT_ID(N'ref.dt23_politica_suficiencia', N'U') IS NULL
    THROW 52301, 'Tabela de política DT23 não instalada', 1;
IF OBJECT_ID(N'ref.dt23_diagnostico_suficiencia', N'U') IS NULL
    THROW 52302, 'Tabela de diagnóstico DT23 não instalada', 1;
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
    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
