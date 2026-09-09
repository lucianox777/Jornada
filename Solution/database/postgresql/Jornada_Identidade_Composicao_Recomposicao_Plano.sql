-- Plano determinístico de recomposição V1.
-- PLANEJADA exige composição APLICADA, mas não significa PUBLICADA e não autoriza writers Gold/Serving.
DO $$
BEGIN
 IF to_regclass('identidade.composicao_aplicacao') IS NULL THEN
  RAISE EXCEPTION 'Aplicação governada de composição deve estar instalada antes do plano de recomposição.';
 END IF;
END $$;

CREATE TABLE IF NOT EXISTS identidade.composicao_recomposicao_plano(
 decision_id UUID PRIMARY KEY REFERENCES identidade.composicao_aplicacao(decision_id),
 composition_request_hash CHAR(64) NOT NULL,
 recomposition_version VARCHAR(80) NOT NULL,
 plan_json TEXT NOT NULL,
 plan_hash CHAR(64) NOT NULL,
 registrado_em TIMESTAMPTZ NOT NULL,
 estado VARCHAR(20) NOT NULL DEFAULT 'PLANEJADA',
 CONSTRAINT ck_composicao_recomposicao_plano_estado CHECK(estado='PLANEJADA'),
 CONSTRAINT ck_composicao_recomposicao_plano_versao CHECK(length(btrim(recomposition_version))>0),
 CONSTRAINT ck_composicao_recomposicao_plano_json CHECK(plan_json IS JSON OBJECT),
 CONSTRAINT ck_composicao_recomposicao_plano_hash CHECK(
   composition_request_hash ~ '^[0-9a-f]{64}$' AND plan_hash ~ '^[0-9a-f]{64}$')
);

DO $$
BEGIN
 IF NOT EXISTS(
   SELECT 1 FROM information_schema.columns
   WHERE table_schema='identidade' AND table_name='composicao_recomposicao_plano' AND column_name='plan_hash') OR
    NOT EXISTS(
   SELECT 1 FROM information_schema.columns
   WHERE table_schema='identidade' AND table_name='composicao_recomposicao_plano' AND column_name='recomposition_version') THEN
   RAISE EXCEPTION 'Schema existente de plano de recomposição é incompatível.';
 END IF;
END $$;

CREATE OR REPLACE FUNCTION identidade.fn_composicao_recomposicao_plano_append_only()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 RAISE EXCEPTION 'Plano de recomposição é append-only.';
END $$;

DROP TRIGGER IF EXISTS tr_composicao_recomposicao_plano_append_only
ON identidade.composicao_recomposicao_plano;
CREATE TRIGGER tr_composicao_recomposicao_plano_append_only
BEFORE UPDATE OR DELETE ON identidade.composicao_recomposicao_plano
FOR EACH STATEMENT EXECUTE FUNCTION identidade.fn_composicao_recomposicao_plano_append_only();
