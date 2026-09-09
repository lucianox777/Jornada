CREATE TABLE IF NOT EXISTS identidade.composicao_publicacao(
    decision_id UUID PRIMARY KEY,
    recomposition_plan_hash CHAR(64) NOT NULL,
    factual_revalidation_version VARCHAR(80) NOT NULL,
    publication_version VARCHAR(80) NOT NULL,
    command_hash CHAR(64) NOT NULL,
    published_by VARCHAR(200) NOT NULL,
    published_at TIMESTAMPTZ NOT NULL,
    mutation_count INTEGER NOT NULL CHECK(mutation_count>=0),
    state VARCHAR(20) NOT NULL CHECK(state='PUBLICADA'),
    CONSTRAINT fk_identidade_composicao_publicacao_recomposicao
        FOREIGN KEY(decision_id) REFERENCES identidade.composicao_recomposicao_plano(decision_id),
    CONSTRAINT ck_identidade_composicao_publicacao_plan_hash
        CHECK(recomposition_plan_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_identidade_composicao_publicacao_command_hash
        CHECK(command_hash ~ '^[0-9a-f]{64}$')
);

CREATE OR REPLACE FUNCTION identidade.fn_composicao_publicacao_append_only()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'identidade.composicao_publicacao e append-only.';
END;
$$;

DROP TRIGGER IF EXISTS tr_composicao_publicacao_append_only ON identidade.composicao_publicacao;
CREATE TRIGGER tr_composicao_publicacao_append_only
BEFORE UPDATE OR DELETE ON identidade.composicao_publicacao
FOR EACH ROW EXECUTE FUNCTION identidade.fn_composicao_publicacao_append_only();
