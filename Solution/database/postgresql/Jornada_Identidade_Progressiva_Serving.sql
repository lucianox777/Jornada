-- Identidade progressiva V1 - projeção Serving/BI.
-- Não ativa Linkage probabilístico, não altera vínculos e não recompõe fatos.
BEGIN;
DO $$
BEGIN
 IF to_regclass('identidade.pessoa_origem_progressiva') IS NULL
    OR to_regclass('silver.pessoa_origem') IS NULL
    OR to_regclass('ref.sistema_origem') IS NULL
    OR to_regclass('ref.gestor') IS NULL THEN
   RAISE EXCEPTION 'Identidade progressiva e referências de origem devem estar instaladas antes da projeção Serving.' USING ERRCODE='P0001';
 END IF;
END $$;

CREATE OR REPLACE VIEW serving.v_identidade_origem_progressiva AS
SELECT
    p.pessoa_origem_id,
    g.codigo AS gestor_codigo,
    so.codigo AS sistema_origem_codigo,
    o.codigo_pessoa_origem,
    p.initial_uuid,
    p.canonical_uuid,
    p.estado,
    p.versao,
    (p.estado='REFERENCIA' AND p.canonical_uuid IS NOT NULL) AS apta_contagem_pessoa_canonica,
    p.criado_em,
    p.atualizado_em,
    p.ultima_resolucao_em
FROM identidade.pessoa_origem_progressiva p
JOIN silver.pessoa_origem o ON o.pessoa_origem_id=p.pessoa_origem_id
JOIN ref.sistema_origem so ON so.sistema_origem_id=o.sistema_origem_id
JOIN ref.gestor g ON g.gestor_id=so.gestor_id;
COMMIT;
