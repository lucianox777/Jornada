-- Jornada PostgreSQL: ruleset imutável de blocking associado ao modelo de linkage.
-- Alterações de regra devem produzir nova versão/modelo.

CREATE TABLE IF NOT EXISTS identidade.linkage_ruleset(
    ruleset_id UUID PRIMARY KEY,
    modelo_id UUID NOT NULL UNIQUE REFERENCES identidade.modelo_linkage(modelo_id),
    ruleset_versao VARCHAR(120) NOT NULL CHECK(BTRIM(ruleset_versao)<>''),
    algoritmo_versao VARCHAR(80) NOT NULL CHECK(BTRIM(algoritmo_versao)<>''),
    fingerprint_sha256 CHAR(64) NOT NULL CHECK(LENGTH(fingerprint_sha256)=64),
    ibge_source_versao VARCHAR(200) NULL,
    ibge_fingerprint_sha256 CHAR(64) NULL,
    criado_em TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT ck_pg_linkage_ruleset_ibge_par CHECK(
        (ibge_source_versao IS NULL AND ibge_fingerprint_sha256 IS NULL)
        OR (ibge_source_versao IS NOT NULL AND ibge_fingerprint_sha256 IS NOT NULL AND LENGTH(ibge_fingerprint_sha256)=64))
);

CREATE TABLE IF NOT EXISTS identidade.linkage_ruleset_passe(
    ruleset_id UUID NOT NULL REFERENCES identidade.linkage_ruleset(ruleset_id),
    passe_ordem INTEGER NOT NULL CHECK(passe_ordem>=0),
    passe_id VARCHAR(120) NOT NULL CHECK(BTRIM(passe_id)<>''),
    PRIMARY KEY(ruleset_id,passe_ordem),
    UNIQUE(ruleset_id,passe_id)
);

CREATE TABLE IF NOT EXISTS identidade.linkage_ruleset_passe_campo(
    ruleset_id UUID NOT NULL,
    passe_ordem INTEGER NOT NULL,
    campo_ordem INTEGER NOT NULL CHECK(campo_ordem>=0),
    atributo VARCHAR(80) NOT NULL CHECK(BTRIM(atributo)<>''),
    PRIMARY KEY(ruleset_id,passe_ordem,campo_ordem),
    UNIQUE(ruleset_id,passe_ordem,atributo),
    FOREIGN KEY(ruleset_id,passe_ordem)
        REFERENCES identidade.linkage_ruleset_passe(ruleset_id,passe_ordem)
);

CREATE OR REPLACE FUNCTION identidade.fn_pg_linkage_ruleset_insert_guard()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE v_status VARCHAR(20);
BEGIN
    SELECT status INTO v_status
      FROM identidade.modelo_linkage
     WHERE modelo_id=NEW.modelo_id
     FOR UPDATE;
    IF v_status IS NULL OR v_status NOT IN('GERANDO','RASCUNHO') THEN
        RAISE EXCEPTION 'Ruleset só pode ser anexado a modelo em GERANDO/RASCUNHO.';
    END IF;
    RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS tr_pg_linkage_ruleset_insert_guard ON identidade.linkage_ruleset;
CREATE TRIGGER tr_pg_linkage_ruleset_insert_guard
BEFORE INSERT ON identidade.linkage_ruleset
FOR EACH ROW EXECUTE FUNCTION identidade.fn_pg_linkage_ruleset_insert_guard();

CREATE OR REPLACE FUNCTION identidade.fn_pg_linkage_ruleset_child_insert_guard()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE v_status VARCHAR(20);
BEGIN
    SELECT m.status INTO v_status
      FROM identidade.linkage_ruleset r
      JOIN identidade.modelo_linkage m ON m.modelo_id=r.modelo_id
     WHERE r.ruleset_id=NEW.ruleset_id
     FOR UPDATE OF m;
    IF v_status IS NULL OR v_status NOT IN('GERANDO','RASCUNHO') THEN
        RAISE EXCEPTION 'Passes/campos só podem ser anexados a ruleset de modelo editável.';
    END IF;
    RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS tr_pg_linkage_ruleset_passe_guard ON identidade.linkage_ruleset_passe;
CREATE TRIGGER tr_pg_linkage_ruleset_passe_guard
BEFORE INSERT ON identidade.linkage_ruleset_passe
FOR EACH ROW EXECUTE FUNCTION identidade.fn_pg_linkage_ruleset_child_insert_guard();

DROP TRIGGER IF EXISTS tr_pg_linkage_ruleset_campo_guard ON identidade.linkage_ruleset_passe_campo;
CREATE TRIGGER tr_pg_linkage_ruleset_campo_guard
BEFORE INSERT ON identidade.linkage_ruleset_passe_campo
FOR EACH ROW EXECUTE FUNCTION identidade.fn_pg_linkage_ruleset_child_insert_guard();

CREATE OR REPLACE FUNCTION identidade.fn_pg_linkage_ruleset_immutable()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'Ruleset de linkage é imutável; gere nova versão/modelo.';
END $$;

DROP TRIGGER IF EXISTS tr_pg_linkage_ruleset_immutable ON identidade.linkage_ruleset;
CREATE TRIGGER tr_pg_linkage_ruleset_immutable
BEFORE UPDATE OR DELETE ON identidade.linkage_ruleset
FOR EACH ROW EXECUTE FUNCTION identidade.fn_pg_linkage_ruleset_immutable();

DROP TRIGGER IF EXISTS tr_pg_linkage_ruleset_passe_immutable ON identidade.linkage_ruleset_passe;
CREATE TRIGGER tr_pg_linkage_ruleset_passe_immutable
BEFORE UPDATE OR DELETE ON identidade.linkage_ruleset_passe
FOR EACH ROW EXECUTE FUNCTION identidade.fn_pg_linkage_ruleset_immutable();

DROP TRIGGER IF EXISTS tr_pg_linkage_ruleset_campo_immutable ON identidade.linkage_ruleset_passe_campo;
CREATE TRIGGER tr_pg_linkage_ruleset_campo_immutable
BEFORE UPDATE OR DELETE ON identidade.linkage_ruleset_passe_campo
FOR EACH ROW EXECUTE FUNCTION identidade.fn_pg_linkage_ruleset_immutable();
