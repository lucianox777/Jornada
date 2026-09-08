-- Evolução aditiva de vocabulário da identidade progressiva.
-- Não altera estados legados de vinculo_fonte, Pessoa, fatos ou resultados de execução.
SET XACT_ABORT ON;
GO
IF OBJECT_ID('identidade.pessoa_origem_progressiva','U') IS NULL OR OBJECT_ID('identidade.pessoa_origem_progressiva_evento','U') IS NULL
 THROW 51140,'Persistência progressiva não instalada.',1;
GO
BEGIN TRANSACTION;
-- Bloqueia escritores durante a troca das constraints e dos valores correntes.
DECLARE @n BIGINT;
SELECT @n=COUNT_BIG(*) FROM identidade.pessoa_origem_progressiva WITH(TABLOCKX,HOLDLOCK);
SELECT @n=COUNT_BIG(*) FROM identidade.pessoa_origem_progressiva_evento WITH(TABLOCKX,HOLDLOCK);
-- O histórico é append-only: sua proteção permanece ativa. Não reescrever recibos antigos.
-- A projeção corrente é normalizada somente depois de validar todos os valores existentes.
IF EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva WHERE estado NOT IN('PROVISORIA','RESOLVIDA','REFERENCIA','INDEFINIDA'))
 OR EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva_evento WHERE estado NOT IN('PROVISORIA','RESOLVIDA','REFERENCIA','INDEFINIDA'))
 THROW 51141,'Estado progressivo desconhecido; migração recusada.',1;
ALTER TABLE identidade.pessoa_origem_progressiva DROP CONSTRAINT ck_progressiva_estado;
ALTER TABLE identidade.pessoa_origem_progressiva_evento DROP CONSTRAINT ck_progressiva_evento_estado;
ALTER TABLE identidade.pessoa_origem_progressiva_evento DROP CONSTRAINT ck_progressiva_evento_criacao;
-- O recibo histórico RESOLVIDA continua válido. Novas escritas usam REFERENCIA.
ALTER TABLE identidade.pessoa_origem_progressiva_evento ADD CONSTRAINT ck_progressiva_evento_estado CHECK(estado IN('PROVISORIA','RESOLVIDA','REFERENCIA','INDEFINIDA'));
ALTER TABLE identidade.pessoa_origem_progressiva_evento ADD CONSTRAINT ck_progressiva_evento_criacao CHECK(
 (tipo='CRIACAO' AND versao=0 AND estado='PROVISORIA' AND canonical_uuid IS NULL AND expected_version IS NULL AND resultado IS NULL AND target_uuid IS NULL AND evidencia_referencia IS NULL AND politica_versao IS NULL AND modelo_versao IS NULL AND universo_referencia IS NULL AND completo IS NULL)
 OR (tipo='RESOLUCAO' AND versao>0 AND expected_version=versao-1 AND completo=1 AND evidencia_referencia IS NOT NULL AND politica_versao IS NOT NULL AND resultado IN('NOVA_IDENTIDADE','ASSOCIACAO_EXISTENTE','INDEFINIDA') AND
 ((resultado='INDEFINIDA' AND estado='INDEFINIDA' AND canonical_uuid IS NULL AND target_uuid IS NULL) OR
 (resultado='NOVA_IDENTIDADE' AND estado IN('RESOLVIDA','REFERENCIA') AND canonical_uuid IS NOT NULL AND target_uuid IS NULL AND universo_referencia IS NOT NULL) OR
 (resultado='ASSOCIACAO_EXISTENTE' AND estado IN('RESOLVIDA','REFERENCIA') AND canonical_uuid=target_uuid AND canonical_uuid IS NOT NULL AND target_uuid IS NOT NULL))));
-- A guarda existente exige recibo com o mesmo estado. Ela é substituída antes da normalização.
EXEC(N'CREATE OR ALTER TRIGGER identidade.tr_progressiva_origem_guard ON identidade.pessoa_origem_progressiva AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.pessoa_origem_id=d.pessoa_origem_id WHERE i.pessoa_origem_id IS NULL)
  THROW 51110,''Referência inicial de origem não pode ser excluída ou transferida.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.pessoa_origem_id=i.pessoa_origem_id WHERE i.initial_uuid<>d.initial_uuid OR i.criado_em<>d.criado_em OR ISNULL(i.legacy_pessoa_uuid,''00000000-0000-0000-0000-000000000000'')<>ISNULL(d.legacy_pessoa_uuid,''00000000-0000-0000-0000-000000000000''))
  THROW 51111,''UUID inicial, origem e referência legada são imutáveis.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.pessoa_origem_id=i.pessoa_origem_id WHERE i.versao<d.versao OR i.versao>d.versao+1 OR (i.versao=d.versao AND (i.estado<>d.estado AND NOT(i.estado=''REFERENCIA'' AND d.estado=''RESOLVIDA'' AND SESSION_CONTEXT(N''''jornada_progressive_reference_migration'''')=1) OR ISNULL(i.canonical_uuid,''00000000-0000-0000-0000-000000000000'')<>ISNULL(d.canonical_uuid,''00000000-0000-0000-0000-000000000000'') OR ISNULL(i.ultimo_destino_externo_uuid,''00000000-0000-0000-0000-000000000000'')<>ISNULL(d.ultimo_destino_externo_uuid,''00000000-0000-0000-0000-000000000000'') OR ISNULL(i.ultima_resolucao_em,''0001-01-01'')<>ISNULL(d.ultima_resolucao_em,''0001-01-01''))))
  THROW 51112,''Alteração de referência exige avanço de uma versão.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.pessoa_origem_id=i.pessoa_origem_id WHERE i.versao<>d.versao AND NOT EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva_evento e WHERE e.pessoa_origem_id=i.pessoa_origem_id AND e.versao=i.versao AND e.tipo=''RESOLUCAO'' AND e.estado=i.estado AND (e.canonical_uuid=i.canonical_uuid OR (e.canonical_uuid IS NULL AND i.canonical_uuid IS NULL)) AND e.ocorrido_em=i.ultima_resolucao_em))
  THROW 51117,''Avanço exige recibo correspondente.'',1;
END;');
EXEC sys.sp_set_session_context @key=N'jornada_progressive_reference_migration',@value=1,@read_only=1;
UPDATE identidade.pessoa_origem_progressiva SET estado='REFERENCIA' WHERE estado='RESOLVIDA';
ALTER TABLE identidade.pessoa_origem_progressiva ADD CONSTRAINT ck_progressiva_estado CHECK(
 (versao=0 AND estado='PROVISORIA' AND canonical_uuid IS NULL AND ultima_resolucao_em IS NULL AND ultimo_destino_externo_uuid IS NULL)
 OR (versao>0 AND ((estado='REFERENCIA' AND canonical_uuid IS NOT NULL) OR (estado='INDEFINIDA' AND canonical_uuid IS NULL)) AND ultima_resolucao_em IS NOT NULL));
COMMIT TRANSACTION;
GO
