-- Aplicação governada da composição V1 para PostgreSQL.
-- O estado APLICADA é representado por recibo append-only; o plano PREPARADA não é mutado.
-- Não ativa Linkage probabilístico e não publica/recompõe Gold/Serving.
BEGIN;
SELECT pg_advisory_xact_lock(hashtextextended('JORNADA:COMPOSICAO:APLICACAO:INSTALL:V1',0));
DO $$
BEGIN
 IF to_regclass('identidade.composicao_plano') IS NULL OR
    to_regclass('identidade.pessoa_origem_progressiva') IS NULL OR
    to_regclass('identidade.pessoa_origem_progressiva_evento') IS NULL THEN
  RAISE EXCEPTION 'Ledger e persistência progressiva devem estar instalados.';
 END IF;
END $$;

CREATE TABLE IF NOT EXISTS identidade.composicao_aplicacao(
 decision_id UUID PRIMARY KEY REFERENCES identidade.composicao_plano(decision_id),
 request_hash CHAR(64) NOT NULL,
 plan_hash CHAR(64) NOT NULL,
 reservas_hash CHAR(64) NOT NULL,
 aplicador_referencia VARCHAR(120) NOT NULL,
 aplicado_em TIMESTAMPTZ NOT NULL,
 alteracoes_aplicadas INTEGER NOT NULL,
 historicos_registrados INTEGER NOT NULL,
 estado VARCHAR(20) NOT NULL DEFAULT 'APLICADA',
 CONSTRAINT ck_composicao_aplicacao_estado CHECK(estado='APLICADA'),
 CONSTRAINT ck_composicao_aplicacao_contagens CHECK(alteracoes_aplicadas>0 AND historicos_registrados>=0),
 CONSTRAINT ck_composicao_aplicacao_aplicador CHECK(length(btrim(aplicador_referencia))>0),
 CONSTRAINT ck_composicao_aplicacao_hash CHECK(
  request_hash ~ '^[0-9a-f]{64}$' AND plan_hash ~ '^[0-9a-f]{64}$' AND reservas_hash ~ '^[0-9a-f]{64}$')
);

CREATE TABLE IF NOT EXISTS identidade.composicao_historico_aplicado(
 decision_id UUID NOT NULL REFERENCES identidade.composicao_plano(decision_id),
 reference_uuid UUID NOT NULL REFERENCES identidade.pessoa(pessoa_uuid),
 members_json TEXT NOT NULL,
 members_hash CHAR(64) NOT NULL,
 registrado_em TIMESTAMPTZ NOT NULL,
 PRIMARY KEY(decision_id,reference_uuid),
 CONSTRAINT ck_composicao_historico_aplicado_json CHECK(
  jsonb_typeof(members_json::jsonb)='array'),
 CONSTRAINT ck_composicao_historico_aplicado_hash CHECK(members_hash ~ '^[0-9a-f]{64}$')
);
CREATE INDEX IF NOT EXISTS ix_composicao_historico_aplicado_referencia
 ON identidade.composicao_historico_aplicado(reference_uuid,registrado_em);

CREATE OR REPLACE FUNCTION identidade.fn_composicao_aplicacao_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
 RAISE EXCEPTION 'Recibo de aplicação é append-only.';
END $$;
DROP TRIGGER IF EXISTS tr_composicao_aplicacao_append_only ON identidade.composicao_aplicacao;
CREATE TRIGGER tr_composicao_aplicacao_append_only
 BEFORE UPDATE OR DELETE ON identidade.composicao_aplicacao
 FOR EACH ROW EXECUTE FUNCTION identidade.fn_composicao_aplicacao_append_only();

CREATE OR REPLACE FUNCTION identidade.fn_composicao_historico_aplicado_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
 RAISE EXCEPTION 'Histórico de composição aplicado é append-only.';
END $$;
DROP TRIGGER IF EXISTS tr_composicao_historico_aplicado_append_only ON identidade.composicao_historico_aplicado;
CREATE TRIGGER tr_composicao_historico_aplicado_append_only
 BEFORE UPDATE OR DELETE ON identidade.composicao_historico_aplicado
 FOR EACH ROW EXECUTE FUNCTION identidade.fn_composicao_historico_aplicado_append_only();

REVOKE ALL ON identidade.composicao_aplicacao,identidade.composicao_historico_aplicado FROM PUBLIC;
COMMIT;
