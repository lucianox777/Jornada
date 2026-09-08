-- Identidade progressiva V1. Aplicar após os três cores operacionais PostgreSQL.
-- Migração aditiva; não ativa Linkage, não altera fatos nem o mapa CPF.
BEGIN;
CREATE TABLE IF NOT EXISTS identidade.pessoa_origem_progressiva(
 pessoa_origem_id BIGINT PRIMARY KEY REFERENCES silver.pessoa_origem(pessoa_origem_id),
 initial_uuid UUID NOT NULL UNIQUE REFERENCES identidade.pessoa(pessoa_uuid),
 canonical_uuid UUID NULL REFERENCES identidade.pessoa(pessoa_uuid),
 legacy_pessoa_uuid UUID NULL REFERENCES identidade.pessoa(pessoa_uuid),
 estado VARCHAR(20) NOT NULL DEFAULT 'PROVISORIA',
 versao BIGINT NOT NULL DEFAULT 0,
 ultima_resolucao_em TIMESTAMPTZ NULL,
 ultimo_destino_externo_uuid UUID NULL REFERENCES identidade.pessoa(pessoa_uuid),
 criado_em TIMESTAMPTZ NOT NULL,
 atualizado_em TIMESTAMPTZ NOT NULL,
 CONSTRAINT ck_progressiva_estado CHECK(
  (versao=0 AND estado='PROVISORIA' AND canonical_uuid IS NULL AND ultima_resolucao_em IS NULL AND ultimo_destino_externo_uuid IS NULL)
  OR (versao>0 AND ((estado='RESOLVIDA' AND canonical_uuid IS NOT NULL) OR (estado='INDEFINIDA' AND canonical_uuid IS NULL)) AND ultima_resolucao_em IS NOT NULL)),
 CONSTRAINT ck_progressiva_externo CHECK(ultimo_destino_externo_uuid IS NULL OR ultimo_destino_externo_uuid<>initial_uuid AND (canonical_uuid IS NULL OR canonical_uuid=ultimo_destino_externo_uuid)),
 CONSTRAINT ck_progressiva_datas CHECK(atualizado_em>=criado_em AND (ultima_resolucao_em IS NULL OR ultima_resolucao_em>=criado_em))
);
CREATE TABLE IF NOT EXISTS identidade.pessoa_origem_progressiva_evento(
 evento_id UUID PRIMARY KEY,
 pessoa_origem_id BIGINT NOT NULL REFERENCES identidade.pessoa_origem_progressiva(pessoa_origem_id),
 versao BIGINT NOT NULL,
 tipo VARCHAR(20) NOT NULL,
 estado VARCHAR(20) NOT NULL,
 canonical_uuid UUID NULL REFERENCES identidade.pessoa(pessoa_uuid),
 expected_version BIGINT NULL,
 resultado VARCHAR(30) NULL,
 target_uuid UUID NULL REFERENCES identidade.pessoa(pessoa_uuid),
 evidencia_referencia VARCHAR(255) NULL,
 politica_versao VARCHAR(120) NULL,
 modelo_versao VARCHAR(120) NULL,
 universo_referencia VARCHAR(255) NULL,
 completo BOOLEAN NULL,
 ocorrido_em TIMESTAMPTZ NOT NULL,
 CONSTRAINT uq_progressiva_evento_versao UNIQUE(pessoa_origem_id,versao),
 CONSTRAINT ck_progressiva_evento_tipo CHECK(tipo IN('CRIACAO','RESOLUCAO')),
 CONSTRAINT ck_progressiva_evento_estado CHECK(estado IN('PROVISORIA','RESOLVIDA','INDEFINIDA')),
 CONSTRAINT ck_progressiva_evento_criacao CHECK(
  (tipo='CRIACAO' AND versao=0 AND estado='PROVISORIA' AND canonical_uuid IS NULL AND expected_version IS NULL AND resultado IS NULL AND target_uuid IS NULL AND evidencia_referencia IS NULL AND politica_versao IS NULL AND modelo_versao IS NULL AND universo_referencia IS NULL AND completo IS NULL)
  OR (tipo='RESOLUCAO' AND versao>0 AND expected_version=versao-1 AND completo=TRUE AND evidencia_referencia IS NOT NULL AND politica_versao IS NOT NULL AND resultado IN('NOVA_IDENTIDADE','ASSOCIACAO_EXISTENTE','INDEFINIDA') AND
   ((resultado='INDEFINIDA' AND estado='INDEFINIDA' AND canonical_uuid IS NULL AND target_uuid IS NULL) OR
    (resultado='NOVA_IDENTIDADE' AND estado='RESOLVIDA' AND canonical_uuid IS NOT NULL AND target_uuid IS NULL AND universo_referencia IS NOT NULL) OR
    (resultado='ASSOCIACAO_EXISTENTE' AND estado='RESOLVIDA' AND canonical_uuid=target_uuid AND canonical_uuid IS NOT NULL AND target_uuid IS NOT NULL))))
);
CREATE OR REPLACE FUNCTION identidade.fn_progressiva_origem_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP='DELETE' THEN
  RAISE EXCEPTION 'Referência inicial de origem não pode ser excluída.';
 END IF;
 IF NEW.pessoa_origem_id IS DISTINCT FROM OLD.pessoa_origem_id OR
    NEW.initial_uuid IS DISTINCT FROM OLD.initial_uuid OR
    NEW.legacy_pessoa_uuid IS DISTINCT FROM OLD.legacy_pessoa_uuid OR
    NEW.criado_em IS DISTINCT FROM OLD.criado_em THEN
  RAISE EXCEPTION 'UUID inicial, origem e referência legada são imutáveis.';
 END IF;
 IF NEW.versao<OLD.versao OR NEW.versao>OLD.versao+1 OR
    (NEW.versao=OLD.versao AND (NEW.estado,NEW.canonical_uuid,NEW.ultima_resolucao_em,NEW.ultimo_destino_externo_uuid)
      IS DISTINCT FROM (OLD.estado,OLD.canonical_uuid,OLD.ultima_resolucao_em,OLD.ultimo_destino_externo_uuid)) THEN
  RAISE EXCEPTION 'Alteração de resolução exige avanço de uma versão.';
 END IF;
 IF NEW.versao<>OLD.versao AND NOT EXISTS(
  SELECT 1 FROM identidade.pessoa_origem_progressiva_evento e
  WHERE e.pessoa_origem_id=NEW.pessoa_origem_id AND e.versao=NEW.versao
    AND e.tipo='RESOLUCAO' AND e.estado=NEW.estado
    AND e.canonical_uuid IS NOT DISTINCT FROM NEW.canonical_uuid
    AND e.ocorrido_em=NEW.ultima_resolucao_em) THEN
  RAISE EXCEPTION 'Avanço exige recibo de resolução correspondente.';
 END IF;
 RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS tr_progressiva_origem_guard ON identidade.pessoa_origem_progressiva;
CREATE TRIGGER tr_progressiva_origem_guard BEFORE UPDATE OR DELETE ON identidade.pessoa_origem_progressiva
FOR EACH ROW EXECUTE FUNCTION identidade.fn_progressiva_origem_guard();
CREATE OR REPLACE FUNCTION identidade.fn_progressiva_evento_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
 RAISE EXCEPTION 'Histórico progressivo é append-only.';
END $$;
DROP TRIGGER IF EXISTS tr_progressiva_evento_append_only ON identidade.pessoa_origem_progressiva_evento;
CREATE TRIGGER tr_progressiva_evento_append_only BEFORE UPDATE OR DELETE ON identidade.pessoa_origem_progressiva_evento
FOR EACH ROW EXECUTE FUNCTION identidade.fn_progressiva_evento_append_only();

-- O lock da origem serializa criação, retries e backfill no mesmo banco.
-- A função participa da transação do chamador e nunca faz commit isolado.
CREATE OR REPLACE FUNCTION identidade.assegurar_origem_progressiva(p_source BIGINT)
RETURNS UUID LANGUAGE plpgsql AS $$
DECLARE
 v_uuid UUID;
 v_legacy UUID;
 v_now TIMESTAMPTZ;
BEGIN
 IF p_source IS NULL OR p_source<=0 THEN
  RAISE EXCEPTION 'Origem inválida.';
 END IF;
 PERFORM 1 FROM silver.pessoa_origem WHERE pessoa_origem_id=p_source FOR UPDATE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Origem inexistente.'; END IF;
 SELECT initial_uuid INTO v_uuid FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=p_source;
 IF FOUND THEN RETURN v_uuid; END IF;
 v_uuid:=gen_random_uuid();
 v_now:=CURRENT_TIMESTAMP;
 -- Última versão, não o último vínculo resolvido de alguma versão antiga.
 SELECT v.pessoa_uuid INTO v_legacy
 FROM silver.pessoa_observacao o
 LEFT JOIN identidade.vinculo_fonte v ON v.pessoa_observacao_id=o.pessoa_observacao_id AND v.ativo AND v.status='RESOLVIDO'
 WHERE o.pessoa_origem_id=p_source
 ORDER BY o.versao_interna DESC,o.pessoa_observacao_id DESC LIMIT 1;
 INSERT INTO identidade.pessoa(pessoa_uuid,status) VALUES(v_uuid,'ATIVO');
 INSERT INTO identidade.pessoa_origem_progressiva(pessoa_origem_id,initial_uuid,legacy_pessoa_uuid,criado_em,atualizado_em)
 VALUES(p_source,v_uuid,v_legacy,v_now,v_now);
 INSERT INTO identidade.pessoa_origem_progressiva_evento(evento_id,pessoa_origem_id,versao,tipo,estado,ocorrido_em)
 VALUES(gen_random_uuid(),p_source,0,'CRIACAO','PROVISORIA',v_now);
 RETURN v_uuid;
END $$;
COMMIT;
-- Backfill deliberadamente separado da instalação: lotes limitados, reentrantes e retomáveis.
