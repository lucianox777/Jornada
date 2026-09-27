-- Teste SQL Server autossuficiente: três gestores e três nomes discordantes.
-- Usa um lote já existente apenas como FK; todas as alterações são revertidas.
-- Executar somente em banco descartável com schema/seed de integração.
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @uuid UNIQUEIDENTIFIER=NEWID(), @lote UNIQUEIDENTIFIER;
DECLARE @gestores TABLE (n INT PRIMARY KEY, gestor_id BIGINT NOT NULL);
INSERT @gestores(n,gestor_id)
SELECT ROW_NUMBER() OVER(ORDER BY gestor_id),gestor_id
FROM (SELECT TOP(3) gestor_id FROM ref.gestor ORDER BY gestor_id) g;
SELECT TOP(1) @lote=lote_id FROM ingestao.lote ORDER BY criado_em;
IF (SELECT COUNT(*) FROM @gestores)<>3 OR @lote IS NULL
 THROW 51000,'Fixture exige tres gestores e um lote no banco descartavel.',1;
DECLARE @obs TABLE(n INT PRIMARY KEY, pessoa_observacao_id BIGINT NOT NULL);
BEGIN TRY
 BEGIN TRANSACTION;
 INSERT identidade.pessoa(pessoa_uuid,status) VALUES(@uuid,'ATIVO');
 DECLARE @n INT=1,@gestor BIGINT,@id BIGINT;
 WHILE @n<=3
 BEGIN
   SELECT @gestor=gestor_id FROM @gestores WHERE n=@n;
   DECLARE @inserted TABLE (pessoa_observacao_id BIGINT);
   DELETE FROM @inserted;
   INSERT silver.pessoa_observacao(
      pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,
      versao_interna,conteudo_hash,cpf,cpf_ausente_motivo,
      nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
   OUTPUT INSERTED.pessoa_observacao_id INTO @inserted
   VALUES(NULL,@lote,@gestor,NULL,1,REPLICATE(CONVERT(VARCHAR(1),@n),64),
      NULL,'SEM_CPF',
      CASE @n WHEN 1 THEN N'ANA SILVA' WHEN 2 THEN N'ANA SOUZA' ELSE N'ANA COSTA' END,
      CASE @n WHEN 1 THEN N'ANA SILVA' WHEN 2 THEN N'ANA SOUZA' ELSE N'ANA COSTA' END,
      '1990-01-01',N'MARIA SILVA',N'MARIA SILVA',
      DATEADD(DAY,-@n,SYSDATETIMEOFFSET()));
   SELECT @id=pessoa_observacao_id FROM @inserted;
   INSERT @obs(n,pessoa_observacao_id) VALUES(@n,@id);
   INSERT identidade.vinculo_fonte(
      pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,
      modelo_id,ativo,resolvido_em,motivo)
   VALUES(@id,@uuid,'CORRECAO_GOVERNADA',NULL,'RESOLVIDO',
      NULL,1,SYSDATETIMEOFFSET(),'FIXTURE_DIVERGENCIA_TRES_FONTES');
   SET @n+=1;
 END;
 EXEC identidade.sp_recompor_gold_pessoa @uuid;
 IF NOT EXISTS(SELECT 1 FROM gold.pessoa
    WHERE pessoa_uuid=@uuid AND estado_concordancia='DIVERGENTE'
      AND fontes_distintas=3)
    THROW 51001,'Gold nao sinalizou divergencia de tres fontes.',1;
 IF (SELECT COUNT(DISTINCT po.nome_cmp)
     FROM @obs o JOIN silver.pessoa_observacao po
       ON po.pessoa_observacao_id=o.pessoa_observacao_id)<>3
    THROW 51002,'Os tres valores originais nao estao consultaveis.',1;
 IF (SELECT COUNT(DISTINCT po.gestor_id)
     FROM @obs o JOIN silver.pessoa_observacao po
       ON po.pessoa_observacao_id=o.pessoa_observacao_id)<>3
    THROW 51003,'Proveniencia dos tres gestores nao preservada.',1;
 -- Sem documento verificado, vence a observacao mais recente (n=1).
 IF NOT EXISTS(SELECT 1 FROM gold.pessoa
    WHERE pessoa_uuid=@uuid AND nome_completo=N'ANA SILVA')
    THROW 51004,'Vencedor da hierarquia foi alterado.',1;
 EXEC identidade.sp_recompor_gold_pessoa @uuid;
 IF (SELECT COUNT(*) FROM @obs o JOIN silver.pessoa_observacao po
     ON po.pessoa_observacao_id=o.pessoa_observacao_id)<>3
    THROW 51005,'Recomposicao repetida perdeu observacoes.',1;
 SELECT o.n,po.pessoa_observacao_id,po.gestor_id,po.nome_completo,
        gp.nome_completo AS valor_gold,gp.estado_concordancia
 FROM @obs o JOIN silver.pessoa_observacao po
   ON po.pessoa_observacao_id=o.pessoa_observacao_id
 JOIN gold.pessoa gp ON gp.pessoa_uuid=@uuid ORDER BY o.n;
 ROLLBACK TRANSACTION;
 PRINT 'PASS: tres fontes discordantes, proveniencia e vencedor preservados.';
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
