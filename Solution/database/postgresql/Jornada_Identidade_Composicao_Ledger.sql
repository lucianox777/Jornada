-- Ledger de preparação de composição V1. Sem aplicação de identidade, vínculos ou Gold.
BEGIN;
SELECT pg_advisory_xact_lock(hashtextextended('JORNADA:COMPOSICAO:LEDGER:INSTALL:V1',0));
DO $$ BEGIN
 IF to_regclass('identidade.pessoa_origem_progressiva') IS NULL OR to_regclass('identidade.pessoa') IS NULL THEN
  RAISE EXCEPTION 'A persistência progressiva deve estar instalada.';
 END IF;
END $$;
CREATE TABLE IF NOT EXISTS identidade.composicao_uuid_reserva(
 reserva_id UUID PRIMARY KEY CHECK(reserva_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 decision_id UUID NOT NULL CHECK(decision_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 pessoa_uuid UUID NOT NULL UNIQUE REFERENCES identidade.pessoa(pessoa_uuid)
  CHECK(pessoa_uuid<>'00000000-0000-0000-0000-000000000000'::uuid),
 criado_em TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS ix_composicao_uuid_reserva_decisao ON identidade.composicao_uuid_reserva(decision_id);
CREATE TABLE IF NOT EXISTS identidade.composicao_plano(
 decision_id UUID PRIMARY KEY,
 request_hash CHAR(64) NOT NULL CHECK(request_hash ~ '^[0-9a-f]{64}$'),
 plan_hash CHAR(64) NOT NULL CHECK(plan_hash ~ '^[0-9a-f]{64}$'),
 reservas_hash CHAR(64) NOT NULL CHECK(reservas_hash ~ '^[0-9a-f]{64}$'),
 request_json TEXT NOT NULL CHECK(jsonb_typeof(request_json::jsonb)='object'),
 plan_json TEXT NOT NULL CHECK(jsonb_typeof(plan_json::jsonb)='object'),
 reservas_json TEXT NOT NULL CHECK(jsonb_typeof(reservas_json::jsonb)='array'),
 solicitante_referencia VARCHAR(120) NOT NULL CHECK(length(btrim(solicitante_referencia))>0),
 correlation_id UUID NULL,
 estado VARCHAR(20) NOT NULL DEFAULT 'PREPARADA' CHECK(estado='PREPARADA'),
 registrado_em TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='identidade' AND table_name='composicao_uuid_reserva' AND column_name='pessoa_uuid') OR
    NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='identidade' AND table_name='composicao_plano' AND column_name='reservas_hash') OR
    NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='identidade' AND table_name='composicao_plano' AND column_name='estado') THEN
  RAISE EXCEPTION 'Schema de ledger existente incompatível.';
 END IF;
END $$;
CREATE OR REPLACE FUNCTION identidade.fn_composicao_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
 RAISE EXCEPTION 'Ledger de composição é append-only.';
END $$;
DROP TRIGGER IF EXISTS tr_composicao_uuid_reserva_append_only ON identidade.composicao_uuid_reserva;
CREATE TRIGGER tr_composicao_uuid_reserva_append_only BEFORE UPDATE OR DELETE ON identidade.composicao_uuid_reserva
FOR EACH ROW EXECUTE FUNCTION identidade.fn_composicao_append_only();
DROP TRIGGER IF EXISTS tr_composicao_plano_append_only ON identidade.composicao_plano;
CREATE TRIGGER tr_composicao_plano_append_only BEFORE UPDATE OR DELETE ON identidade.composicao_plano
FOR EACH ROW EXECUTE FUNCTION identidade.fn_composicao_append_only();

CREATE OR REPLACE FUNCTION identidade.reservar_uuid_composicao(p_decision UUID,p_reserva UUID)
RETURNS UUID LANGUAGE plpgsql AS $$
DECLARE v_uuid UUID;v_owner UUID;
BEGIN
 IF p_decision IS NULL OR p_decision='00000000-0000-0000-0000-000000000000'::uuid OR
    p_reserva IS NULL OR p_reserva='00000000-0000-0000-0000-000000000000'::uuid THEN
  RAISE EXCEPTION 'Identificadores de reserva inválidos.';
 END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('JORNADA:COMPOSICAO:DECISAO:'||p_decision::text,0));
 SELECT pessoa_uuid,decision_id INTO v_uuid,v_owner FROM identidade.composicao_uuid_reserva WHERE reserva_id=p_reserva FOR UPDATE;
 IF FOUND THEN
  IF v_owner<>p_decision THEN RAISE EXCEPTION 'Reserva pertence a outra decisão.'; END IF;
  RETURN v_uuid;
 END IF;
 PERFORM 1 FROM identidade.composicao_plano WHERE decision_id=p_decision FOR UPDATE;
 IF FOUND THEN RAISE EXCEPTION 'Não é permitido acrescentar reservas após registrar o plano.'; END IF;
 v_uuid:=gen_random_uuid();
 INSERT INTO identidade.pessoa(pessoa_uuid,status) VALUES(v_uuid,'ATIVO');
 INSERT INTO identidade.composicao_uuid_reserva(reserva_id,decision_id,pessoa_uuid) VALUES(p_reserva,p_decision,v_uuid);
 RETURN v_uuid;
END $$;

CREATE OR REPLACE FUNCTION identidade.registrar_plano_composicao(
 p_decision UUID,p_request TEXT,p_plan TEXT,p_reservas TEXT,p_solicitante VARCHAR(120),p_correlation UUID DEFAULT NULL)
RETURNS TEXT LANGUAGE plpgsql AS $$
DECLARE
 v_request JSONB;v_plan JSONB;v_reservas JSONB;
 v_hash TEXT;v_plan_hash TEXT;v_reservas_hash TEXT;
 v_old RECORD;v_count BIGINT;v_unique BIGINT;
BEGIN
 IF p_decision IS NULL OR p_decision='00000000-0000-0000-0000-000000000000'::uuid OR
    p_request IS NULL OR p_plan IS NULL OR p_reservas IS NULL OR p_solicitante IS NULL OR btrim(p_solicitante)='' THEN
  RAISE EXCEPTION 'Registro de plano incompleto.';
 END IF;
 v_request:=p_request::jsonb;v_plan:=p_plan::jsonb;v_reservas:=p_reservas::jsonb;
 IF jsonb_typeof(v_request)<>'object' OR jsonb_typeof(v_plan)<>'object' OR jsonb_typeof(v_reservas)<>'array' OR
    (v_request->>'DecisionId')::uuid IS DISTINCT FROM p_decision OR
    (v_plan->>'DecisionId')::uuid IS DISTINCT FROM p_decision OR
    v_request->>'Operation' IS NULL OR v_request->>'Operation' NOT IN('0','1','2') OR
    nullif(btrim(v_request->>'EvidenceReference'),'') IS NULL OR
    nullif(btrim(v_request->>'PolicyVersion'),'') IS NULL OR
    v_request->>'DecidedAt' IS NULL OR
    jsonb_typeof(v_request->'Assignments')<>'array' OR
    jsonb_typeof(v_plan->'Changes')<>'array' OR jsonb_typeof(v_plan->'HistoryToAppend')<>'array' THEN
  RAISE EXCEPTION 'Payload de composição incompatível.';
 END IF;
 v_hash:=encode(sha256(convert_to(p_request,'UTF8')),'hex');
 v_plan_hash:=encode(sha256(convert_to(p_plan,'UTF8')),'hex');
 v_reservas_hash:=encode(sha256(convert_to(p_reservas,'UTF8')),'hex');
 IF v_plan->>'RequestHash' IS DISTINCT FROM v_hash THEN RAISE EXCEPTION 'Hash do plano não corresponde à decisão.'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('JORNADA:COMPOSICAO:DECISAO:'||p_decision::text,0));
 SELECT request_hash,plan_hash,reservas_hash INTO v_old FROM identidade.composicao_plano WHERE decision_id=p_decision FOR UPDATE;
 IF FOUND THEN
  IF v_old.request_hash<>v_hash OR v_old.plan_hash<>v_plan_hash OR v_old.reservas_hash<>v_reservas_hash THEN
   RAISE EXCEPTION 'decision_id reutilizado com conteúdo diferente.';
  END IF;
  RETURN v_hash;
 END IF;
 SELECT COUNT(*),COUNT(DISTINCT value::uuid) INTO v_count,v_unique FROM jsonb_array_elements_text(v_reservas) AS r(value);
 IF v_count<>v_unique OR EXISTS(
  SELECT 1 FROM jsonb_array_elements_text(v_reservas) AS r(value)
  LEFT JOIN identidade.composicao_uuid_reserva x ON x.pessoa_uuid=r.value::uuid AND x.decision_id=p_decision
  WHERE x.reserva_id IS NULL) OR EXISTS(
  SELECT 1 FROM identidade.composicao_uuid_reserva x WHERE x.decision_id=p_decision AND NOT EXISTS(
   SELECT 1 FROM jsonb_array_elements_text(v_reservas) AS r(value) WHERE r.value::uuid=x.pessoa_uuid)) THEN
  RAISE EXCEPTION 'Conjunto de reservas não corresponde à decisão.';
 END IF;
 INSERT INTO identidade.composicao_plano(decision_id,request_hash,plan_hash,reservas_hash,request_json,plan_json,reservas_json,solicitante_referencia,correlation_id)
 VALUES(p_decision,v_hash,v_plan_hash,v_reservas_hash,p_request,p_plan,p_reservas,p_solicitante,p_correlation);
 RETURN v_hash;
END $$;
-- Nenhuma concessão nova de acesso à aplicação; o executor governado terá grants explícitos.
REVOKE ALL ON identidade.composicao_uuid_reserva,identidade.composicao_plano FROM PUBLIC;
REVOKE ALL ON FUNCTION identidade.reservar_uuid_composicao(UUID,UUID) FROM PUBLIC;
REVOKE ALL ON FUNCTION identidade.registrar_plano_composicao(UUID,TEXT,TEXT,TEXT,VARCHAR,UUID) FROM PUBLIC;
COMMIT;