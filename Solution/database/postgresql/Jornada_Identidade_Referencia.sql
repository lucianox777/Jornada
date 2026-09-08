-- Evolução V2 da identidade progressiva. Histórico append-only preservado.
BEGIN;
DO $$
BEGIN
 IF to_regclass('identidade.pessoa_origem_progressiva') IS NULL OR to_regclass('identidade.pessoa_origem_progressiva_evento') IS NULL THEN
  RAISE EXCEPTION 'Persistência progressiva não instalada.';
 END IF;
END $$;
LOCK TABLE identidade.pessoa_origem_progressiva, identidade.pessoa_origem_progressiva_evento IN ACCESS EXCLUSIVE MODE;
DO $$
BEGIN
 IF EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva WHERE estado NOT IN('PROVISORIA','RESOLVIDA','REFERENCIA','INDEFINIDA')) OR EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva_evento WHERE estado NOT IN('PROVISORIA','RESOLVIDA','REFERENCIA','INDEFINIDA')) THEN
  RAISE EXCEPTION 'Estado progressivo desconhecido; migração recusada.';
 END IF;
END $$;
-- Remoção temporária da guarda somente dentro da transação bloqueada.
DROP TRIGGER tr_progressiva_origem_guard ON identidade.pessoa_origem_progressiva;
ALTER TABLE identidade.pessoa_origem_progressiva DROP CONSTRAINT ck_progressiva_estado;
ALTER TABLE identidade.pessoa_origem_progressiva_evento DROP CONSTRAINT ck_progressiva_evento_estado;
ALTER TABLE identidade.pessoa_origem_progressiva_evento DROP CONSTRAINT ck_progressiva_evento_criacao;
UPDATE identidade.pessoa_origem_progressiva SET estado='REFERENCIA' WHERE estado='RESOLVIDA';
ALTER TABLE identidade.pessoa_origem_progressiva ADD CONSTRAINT ck_progressiva_estado CHECK(
 (versao=0 AND estado='PROVISORIA' AND canonical_uuid IS NULL AND ultima_resolucao_em IS NULL AND ultimo_destino_externo_uuid IS NULL)
 OR (versao>0 AND ((estado='REFERENCIA' AND canonical_uuid IS NOT NULL) OR (estado='INDEFINIDA' AND canonical_uuid IS NULL)) AND ultima_resolucao_em IS NOT NULL));
ALTER TABLE identidade.pessoa_origem_progressiva_evento ADD CONSTRAINT ck_progressiva_evento_estado CHECK(estado IN('PROVISORIA','RESOLVIDA','REFERENCIA','INDEFINIDA'));
ALTER TABLE identidade.pessoa_origem_progressiva_evento ADD CONSTRAINT ck_progressiva_evento_criacao CHECK(
 (tipo='CRIACAO' AND versao=0 AND estado='PROVISORIA' AND canonical_uuid IS NULL AND expected_version IS NULL AND resultado IS NULL AND target_uuid IS NULL AND evidencia_referencia IS NULL AND politica_versao IS NULL AND modelo_versao IS NULL AND universo_referencia IS NULL AND completo IS NULL)
 OR (tipo='RESOLUCAO' AND versao>0 AND expected_version=versao-1 AND completo=TRUE AND evidencia_referencia IS NOT NULL AND politica_versao IS NOT NULL AND resultado IN('NOVA_IDENTIDADE','ASSOCIACAO_EXISTENTE','INDEFINIDA') AND
 ((resultado='INDEFINIDA' AND estado='INDEFINIDA' AND canonical_uuid IS NULL AND target_uuid IS NULL) OR
 (resultado='NOVA_IDENTIDADE' AND estado IN('RESOLVIDA','REFERENCIA') AND canonical_uuid IS NOT NULL AND target_uuid IS NULL AND universo_referencia IS NOT NULL) OR
 (resultado='ASSOCIACAO_EXISTENTE' AND estado IN('RESOLVIDA','REFERENCIA') AND canonical_uuid=target_uuid AND canonical_uuid IS NOT NULL AND target_uuid IS NOT NULL))));
CREATE OR REPLACE FUNCTION identidade.fn_progressiva_origem_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Referência inicial de origem não pode ser excluída.'; END IF;
 IF NEW.pessoa_origem_id IS DISTINCT FROM OLD.pessoa_origem_id OR NEW.initial_uuid IS DISTINCT FROM OLD.initial_uuid OR NEW.legacy_pessoa_uuid IS DISTINCT FROM OLD.legacy_pessoa_uuid OR NEW.criado_em IS DISTINCT FROM OLD.criado_em THEN
  RAISE EXCEPTION 'UUID inicial, origem e referência legada são imutáveis.';
 END IF;
 IF NEW.versao<OLD.versao OR NEW.versao>OLD.versao+1 OR (NEW.versao=OLD.versao AND (NEW.estado,NEW.canonical_uuid,NEW.ultima_resolucao_em,NEW.ultimo_destino_externo_uuid) IS DISTINCT FROM (OLD.estado,OLD.canonical_uuid,OLD.ultima_resolucao_em,OLD.ultimo_destino_externo_uuid)) THEN
  RAISE EXCEPTION 'Alteração de referência exige avanço de uma versão.';
 END IF;
 IF NEW.versao<>OLD.versao AND NOT EXISTS(
  SELECT 1 FROM identidade.pessoa_origem_progressiva_evento e WHERE e.pessoa_origem_id=NEW.pessoa_origem_id AND e.versao=NEW.versao AND e.tipo='RESOLUCAO' AND (e.estado=NEW.estado OR (e.estado='RESOLVIDA' AND NEW.estado='REFERENCIA')) AND e.canonical_uuid IS NOT DISTINCT FROM NEW.canonical_uuid AND e.ocorrido_em=NEW.ultima_resolucao_em) THEN
  RAISE EXCEPTION 'Avanço exige recibo correspondente.';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER tr_progressiva_origem_guard BEFORE UPDATE OR DELETE ON identidade.pessoa_origem_progressiva FOR EACH ROW EXECUTE FUNCTION identidade.fn_progressiva_origem_guard();
COMMIT;
