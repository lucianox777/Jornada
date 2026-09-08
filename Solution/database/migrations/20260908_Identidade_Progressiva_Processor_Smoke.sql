SET XACT_ABORT ON;
GO
DECLARE @sistema BIGINT,@lote UNIQUEIDENTIFIER,@gestor BIGINT;
SELECT TOP(1) @sistema=o.sistema_origem_id,@lote=po.lote_id,@gestor=po.gestor_id
FROM silver.pessoa_observacao po
JOIN silver.pessoa_origem o ON o.pessoa_origem_id=po.pessoa_origem_id
ORDER BY po.pessoa_observacao_id;
IF @sistema IS NULL OR @lote IS NULL OR @gestor IS NULL THROW 51140,'Fixture base insuficiente para smoke de cutover.',1;

DECLARE @code NVARCHAR(255)=N'CUTOVER-COMMIT-20260908';
DECLARE @source BIGINT,@obs BIGINT,@uuid UNIQUEIDENTIFIER,@uuid2 UNIQUEIDENTIFIER;
IF EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE codigo_pessoa_origem=@code) THROW 51141,'Smoke requer banco limpo.',1;

CREATE TABLE #cutover_obs(pessoa_observacao_id BIGINT NOT NULL);
BEGIN TRANSACTION;
INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem) VALUES(@sistema,@code);
SET @source=CONVERT(BIGINT,SCOPE_IDENTITY());
INSERT silver.pessoa_observacao(
 pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
 cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
OUTPUT INSERTED.pessoa_observacao_id INTO #cutover_obs
VALUES(@source,@lote,@gestor,@code,1,REPLICATE('a',64),NULL,'SEM_CPF',N'PESSOA CUTOVER',N'PESSOA CUTOVER','1990-01-01',N'MAE CUTOVER',N'MAE CUTOVER',SYSDATETIMEOFFSET());
SELECT TOP(1) @obs=pessoa_observacao_id FROM #cutover_obs;
INSERT identidade.vinculo_fonte(pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
VALUES(@obs,NULL,'PENDENTE_PROBABILISTICO',NULL,'NAO_RESOLVIDO',NULL,1,NULL,'AGUARDA_LINKAGE_SOB_DEMANDA');
SELECT @uuid=initial_uuid FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=@source;
IF @uuid IS NULL THROW 51142,'Novo vínculo não materializou initial_uuid.',1;
IF (SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento WHERE pessoa_origem_id=@source AND tipo='CRIACAO')<>1
 THROW 51143,'Criação inicial não produziu exatamente um evento.',1;
COMMIT TRANSACTION;
DROP TABLE #cutover_obs;

-- Nova versão da mesma origem deve preservar a referência inicial sem duplicar evento.
CREATE TABLE #cutover_obs2(pessoa_observacao_id BIGINT NOT NULL);
BEGIN TRANSACTION;
INSERT silver.pessoa_observacao(
 pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
 cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
OUTPUT INSERTED.pessoa_observacao_id INTO #cutover_obs2
VALUES(@source,@lote,@gestor,@code,2,REPLICATE('b',64),NULL,'SEM_CPF',N'PESSOA CUTOVER',N'PESSOA CUTOVER','1990-01-01',N'MAE CUTOVER',N'MAE CUTOVER',SYSDATETIMEOFFSET());
SELECT TOP(1) @obs=pessoa_observacao_id FROM #cutover_obs2;
INSERT identidade.vinculo_fonte(pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
VALUES(@obs,NULL,'PENDENTE_PROBABILISTICO',NULL,'NAO_RESOLVIDO',NULL,1,NULL,'AGUARDA_LINKAGE_SOB_DEMANDA');
SELECT @uuid2=initial_uuid FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=@source;
IF @uuid2<>@uuid THROW 51144,'Nova versão alterou initial_uuid.',1;
IF (SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento WHERE pessoa_origem_id=@source AND tipo='CRIACAO')<>1
 THROW 51145,'Nova versão duplicou evento de criação.',1;
COMMIT TRANSACTION;
DROP TABLE #cutover_obs2;

-- CPF determinístico publica REFERENCIA na mesma transação e grava um único recibo.
DECLARE @canonical UNIQUEIDENTIFIER=NEWID();
INSERT identidade.pessoa(pessoa_uuid,status) VALUES(@canonical,'ATIVO');
CREATE TABLE #cutover_ref_obs(pessoa_observacao_id BIGINT NOT NULL);
BEGIN TRANSACTION;
INSERT silver.pessoa_observacao(
 pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
 cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
OUTPUT INSERTED.pessoa_observacao_id INTO #cutover_ref_obs
VALUES(@source,@lote,@gestor,@code,3,REPLICATE('d',64),'52998224725',NULL,N'PESSOA CUTOVER',N'PESSOA CUTOVER','1990-01-01',N'MAE CUTOVER',N'MAE CUTOVER',SYSDATETIMEOFFSET());
SELECT TOP(1) @obs=pessoa_observacao_id FROM #cutover_ref_obs;
INSERT identidade.vinculo_fonte(pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
VALUES(@obs,@canonical,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,1,SYSDATETIMEOFFSET(),'CPF_DETERMINISTICO');
IF NOT EXISTS(
 SELECT 1 FROM identidade.pessoa_origem_progressiva
 WHERE pessoa_origem_id=@source AND estado='REFERENCIA' AND canonical_uuid=@canonical AND versao=1 AND ultima_resolucao_em IS NOT NULL)
 THROW 51150,'CPF determinístico não publicou REFERENCIA versão 1.',1;
IF (SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento
    WHERE pessoa_origem_id=@source AND tipo='RESOLUCAO' AND versao=1 AND estado='REFERENCIA'
      AND canonical_uuid=@canonical AND target_uuid=@canonical AND resultado='ASSOCIACAO_EXISTENTE'
      AND evidencia_referencia=N'CPF_ANCORA_DETERMINISTICA' AND politica_versao=N'CPF_ANCORA_V1' AND completo=1)<>1
 THROW 51151,'REFERENCIA não produziu recibo determinístico esperado.',1;
COMMIT TRANSACTION;
DROP TABLE #cutover_ref_obs;

-- Repetição com o mesmo UUID é idempotente: não avança versão nem duplica recibo.
CREATE TABLE #cutover_ref_obs2(pessoa_observacao_id BIGINT NOT NULL);
BEGIN TRANSACTION;
INSERT silver.pessoa_observacao(
 pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
 cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
OUTPUT INSERTED.pessoa_observacao_id INTO #cutover_ref_obs2
VALUES(@source,@lote,@gestor,@code,4,REPLICATE('e',64),'52998224725',NULL,N'PESSOA CUTOVER',N'PESSOA CUTOVER','1990-01-01',N'MAE CUTOVER',N'MAE CUTOVER',SYSDATETIMEOFFSET());
SELECT TOP(1) @obs=pessoa_observacao_id FROM #cutover_ref_obs2;
INSERT identidade.vinculo_fonte(pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
VALUES(@obs,@canonical,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,1,SYSDATETIMEOFFSET(),'CPF_DETERMINISTICO_REPLAY');
IF NOT EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=@source AND estado='REFERENCIA' AND canonical_uuid=@canonical AND versao=1)
 THROW 51152,'Replay determinístico alterou a referência.',1;
IF (SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento WHERE pessoa_origem_id=@source AND tipo='RESOLUCAO')<>1
 THROW 51153,'Replay determinístico duplicou recibo de resolução.',1;
COMMIT TRANSACTION;
DROP TABLE #cutover_ref_obs2;

-- Tentativa de transferir a mesma origem para outro UUID deve falhar fechada e reverter tudo.
DECLARE @conflicting UNIQUEIDENTIFIER=NEWID();
CREATE TABLE #cutover_conflict_obs(pessoa_observacao_id BIGINT NOT NULL);
BEGIN TRY
 BEGIN TRANSACTION;
 INSERT identidade.pessoa(pessoa_uuid,status) VALUES(@conflicting,'ATIVO');
 INSERT silver.pessoa_observacao(
  pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
  cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
 OUTPUT INSERTED.pessoa_observacao_id INTO #cutover_conflict_obs
 VALUES(@source,@lote,@gestor,@code,5,REPLICATE('f',64),'52998224725',NULL,N'PESSOA CUTOVER',N'PESSOA CUTOVER','1990-01-01',N'MAE CUTOVER',N'MAE CUTOVER',SYSDATETIMEOFFSET());
 SELECT TOP(1) @obs=pessoa_observacao_id FROM #cutover_conflict_obs;
 INSERT identidade.vinculo_fonte(pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
 VALUES(@obs,@conflicting,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,1,SYSDATETIMEOFFSET(),'CPF_DETERMINISTICO_DIVERGENTE');
 COMMIT TRANSACTION;
 THROW 51154,'Transferência indevida de REFERENCIA foi aceita.',1;
END TRY
BEGIN CATCH
 IF ERROR_NUMBER()=51154 THROW;
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 IF ERROR_NUMBER()<>51121 THROW;
END CATCH;
DROP TABLE #cutover_conflict_obs;
IF EXISTS(SELECT 1 FROM identidade.pessoa WHERE pessoa_uuid=@conflicting) THROW 51155,'Falha fechada deixou Pessoa conflitante.',1;
IF NOT EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=@source AND estado='REFERENCIA' AND canonical_uuid=@canonical AND versao=1)
 THROW 51156,'Falha fechada alterou a REFERENCIA consolidada.',1;
IF (SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento WHERE pessoa_origem_id=@source AND tipo='RESOLUCAO')<>1
 THROW 51157,'Falha fechada deixou recibo espúrio.',1;

-- Uma transação abortada não pode deixar origem, initial_uuid nem Pessoa órfã.
DECLARE @rollbackCode NVARCHAR(255)=N'CUTOVER-ROLLBACK-20260908';
DECLARE @rollbackSource BIGINT,@rollbackObs BIGINT,@rollbackUuid UNIQUEIDENTIFIER;
CREATE TABLE #cutover_rollback_obs(pessoa_observacao_id BIGINT NOT NULL);
BEGIN TRANSACTION;
INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem) VALUES(@sistema,@rollbackCode);
SET @rollbackSource=CONVERT(BIGINT,SCOPE_IDENTITY());
INSERT silver.pessoa_observacao(
 pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
 cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
OUTPUT INSERTED.pessoa_observacao_id INTO #cutover_rollback_obs
VALUES(@rollbackSource,@lote,@gestor,@rollbackCode,1,REPLICATE('c',64),NULL,'SEM_CPF',N'PESSOA ROLLBACK',N'PESSOA ROLLBACK','1991-01-01',N'MAE ROLLBACK',N'MAE ROLLBACK',SYSDATETIMEOFFSET());
SELECT TOP(1) @rollbackObs=pessoa_observacao_id FROM #cutover_rollback_obs;
INSERT identidade.vinculo_fonte(pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
VALUES(@rollbackObs,NULL,'PENDENTE_PROBABILISTICO',NULL,'NAO_RESOLVIDO',NULL,1,NULL,'AGUARDA_LINKAGE_SOB_DEMANDA');
SELECT @rollbackUuid=initial_uuid FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=@rollbackSource;
IF @rollbackUuid IS NULL THROW 51146,'Fixture rollback não materializou initial_uuid antes do abort.',1;
ROLLBACK TRANSACTION;
DROP TABLE #cutover_rollback_obs;
IF EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE codigo_pessoa_origem=@rollbackCode) THROW 51147,'Rollback deixou origem Silver.',1;
IF EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=@rollbackSource) THROW 51148,'Rollback deixou referência progressiva.',1;
IF EXISTS(SELECT 1 FROM identidade.pessoa WHERE pessoa_uuid=@rollbackUuid) THROW 51149,'Rollback deixou Pessoa órfã.',1;

PRINT 'PROGRESSIVE PROCESSOR CUTOVER SQLSERVER: OK';
GO
