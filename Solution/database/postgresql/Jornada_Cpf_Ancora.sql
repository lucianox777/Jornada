-- Âncora CPF imutável, migração opt-in. Exige a base PostgreSQL operacional instalada.
CREATE OR REPLACE FUNCTION identidade.fn_cpf_ancora_valido(p_cpf TEXT)
RETURNS BOOLEAN LANGUAGE plpgsql IMMUTABLE STRICT AS $$
DECLARE i INTEGER; s INTEGER; d INTEGER; expected INTEGER;
BEGIN
 IF p_cpf !~ '^[0-9]{11}$' OR p_cpf=repeat(substr(p_cpf,1,1),11) THEN RETURN FALSE; END IF;
 s:=0;
 FOR i IN 1..9 LOOP s:=s+substr(p_cpf,i,1)::INTEGER*(11-i); END LOOP;
 d:=s%11; expected:=CASE WHEN d<2 THEN 0 ELSE 11-d END;
 IF expected<>substr(p_cpf,10,1)::INTEGER THEN RETURN FALSE; END IF;
 s:=0;
 FOR i IN 1..10 LOOP s:=s+substr(p_cpf,i,1)::INTEGER*(12-i); END LOOP;
 d:=s%11; expected:=CASE WHEN d<2 THEN 0 ELSE 11-d END;
 RETURN expected=substr(p_cpf,11,1)::INTEGER;
END;
$$;
BEGIN;
LOCK TABLE identidade.identity_map IN SHARE ROW EXCLUSIVE MODE;
DO $$
BEGIN
 IF EXISTS(SELECT 1 FROM identidade.identity_map WHERE tipo='CPF' AND
   (identificador IS NULL OR NOT identidade.fn_cpf_ancora_valido(identificador) OR
    length(identificador)<>11 OR pessoa_uuid IS NULL OR pessoa_uuid='00000000-0000-0000-0000-000000000000'::uuid)) THEN
  RAISE EXCEPTION 'Histórico CPF inválido: reconciliação explícita necessária.';
 END IF;
 IF EXISTS(SELECT 1 FROM identidade.identity_map WHERE tipo='CPF' GROUP BY identificador HAVING count(DISTINCT pessoa_uuid)>1) THEN
  RAISE EXCEPTION 'Um CPF possui UUIDs históricos distintos; migração interrompida.';
 END IF;
 IF EXISTS(SELECT 1 FROM identidade.identity_map WHERE tipo='CPF' GROUP BY pessoa_uuid HAVING count(DISTINCT identificador)>1) THEN
  RAISE EXCEPTION 'Um UUID possui CPFs históricos distintos; migração interrompida.';
 END IF;
END;
$$;
CREATE TABLE IF NOT EXISTS identidade.cpf_ancora(
 cpf CHAR(11) COLLATE "C" PRIMARY KEY,
 pessoa_uuid UUID NOT NULL UNIQUE REFERENCES identidade.pessoa(pessoa_uuid),
 criado_em TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
 CONSTRAINT ck_pg_cpf_ancora_valido CHECK(identidade.fn_cpf_ancora_valido(cpf)),
 CONSTRAINT ck_pg_cpf_ancora_uuid CHECK(pessoa_uuid<>'00000000-0000-0000-0000-000000000000'::uuid)
);
DO $$
BEGIN
 IF EXISTS(SELECT 1 FROM identidade.cpf_ancora a JOIN identidade.identity_map m ON m.tipo='CPF' AND m.identificador=a.cpf WHERE m.pessoa_uuid<>a.pessoa_uuid) THEN
  RAISE EXCEPTION 'Âncora existente diverge do histórico.';
 END IF;
 IF EXISTS(SELECT 1 FROM identidade.cpf_ancora a JOIN identidade.identity_map m ON m.tipo='CPF' AND m.pessoa_uuid=a.pessoa_uuid WHERE m.identificador<>a.cpf) THEN
  RAISE EXCEPTION 'UUID reservado possui outro CPF histórico.';
 END IF;
END;
$$;
INSERT INTO identidade.cpf_ancora(cpf,pessoa_uuid)
SELECT DISTINCT m.identificador::CHAR(11),m.pessoa_uuid
FROM identidade.identity_map m WHERE m.tipo='CPF'
ON CONFLICT(cpf) DO NOTHING;
CREATE OR REPLACE FUNCTION identidade.fn_cpf_ancora_imutavel() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
 RAISE EXCEPTION 'Âncora CPF imutável: correções devem reatribuir registros, não transferir a âncora.';
END;
$$;
DROP TRIGGER IF EXISTS tr_cpf_ancora_imutavel ON identidade.cpf_ancora;
CREATE TRIGGER tr_cpf_ancora_imutavel BEFORE UPDATE OR DELETE ON identidade.cpf_ancora
FOR EACH ROW EXECUTE FUNCTION identidade.fn_cpf_ancora_imutavel();
CREATE OR REPLACE FUNCTION identidade.fn_obter_cpf_ancora(p_cpf TEXT)
RETURNS UUID LANGUAGE plpgsql STABLE AS $$
DECLARE v_uuid UUID;
BEGIN
 IF p_cpf IS NULL OR NOT identidade.fn_cpf_ancora_valido(p_cpf) THEN
  RAISE EXCEPTION 'CPF inválido.';
 END IF;
 SELECT pessoa_uuid INTO v_uuid FROM identidade.cpf_ancora WHERE cpf=p_cpf;
 RETURN v_uuid;
END;
$$;
CREATE OR REPLACE FUNCTION identidade.fn_identity_map_cpf_ancora() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE v_uuid UUID; v_cpf CHAR(11);
BEGIN
 IF TG_OP='UPDATE' AND (NEW.tipo='CPF' OR OLD.tipo='CPF') AND
    (NEW.tipo IS DISTINCT FROM OLD.tipo OR NEW.identificador IS DISTINCT FROM OLD.identificador OR NEW.pessoa_uuid IS DISTINCT FROM OLD.pessoa_uuid) THEN
  RAISE EXCEPTION 'Não é permitido transferir a identidade de um mapa CPF histórico.';
 END IF;
 IF NEW.tipo<>'CPF' THEN RETURN NEW; END IF;
 IF NOT identidade.fn_cpf_ancora_valido(NEW.identificador) OR length(NEW.identificador)<>11 OR
    NEW.pessoa_uuid='00000000-0000-0000-0000-000000000000'::uuid THEN
  RAISE EXCEPTION 'Vínculo CPF inválido.';
 END IF;
 INSERT INTO identidade.cpf_ancora(cpf,pessoa_uuid) VALUES(NEW.identificador::CHAR(11),NEW.pessoa_uuid)
 ON CONFLICT(cpf) DO NOTHING;
 SELECT pessoa_uuid INTO v_uuid FROM identidade.cpf_ancora WHERE cpf=NEW.identificador FOR UPDATE;
 IF v_uuid IS DISTINCT FROM NEW.pessoa_uuid THEN RAISE EXCEPTION 'CPF já possui outro UUID permanente.'; END IF;
 SELECT cpf INTO v_cpf FROM identidade.cpf_ancora WHERE pessoa_uuid=NEW.pessoa_uuid;
 IF v_cpf IS DISTINCT FROM NEW.identificador THEN RAISE EXCEPTION 'UUID já possui outro CPF permanente.'; END IF;
 RETURN NEW;
END;
$$;
DROP TRIGGER IF EXISTS tr_identity_map_cpf_ancora ON identidade.identity_map;
CREATE TRIGGER tr_identity_map_cpf_ancora BEFORE INSERT OR UPDATE ON identidade.identity_map
FOR EACH ROW EXECUTE FUNCTION identidade.fn_identity_map_cpf_ancora();
COMMIT;
