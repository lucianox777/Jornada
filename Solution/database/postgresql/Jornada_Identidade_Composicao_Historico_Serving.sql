-- Projeção read-only do histórico de composição efetivamente publicado.
-- Não escolhe sucessor canônico, não altera identidade, Gold/Serving factual ou Linkage.
BEGIN;
DO $$
BEGIN
 IF to_regclass('identidade.composicao_historico_aplicado') IS NULL OR
    to_regclass('identidade.composicao_publicacao') IS NULL THEN
  RAISE EXCEPTION 'Histórico aplicado e publicação de composição devem estar instalados.';
 END IF;
END $$;

CREATE OR REPLACE VIEW serving.v_identidade_composicao_historico_publicado AS
SELECT
 h.decision_id,
 h.reference_uuid,
 (m.value #>> '{}')::uuid AS member_uuid,
 h.registrado_em,
 p.published_at,
 p.publication_version
FROM identidade.composicao_historico_aplicado h
JOIN identidade.composicao_publicacao p
  ON p.decision_id=h.decision_id
 AND p.state='PUBLICADA'
CROSS JOIN LATERAL jsonb_array_elements(h.members_json::jsonb) AS m(value);

COMMIT;
