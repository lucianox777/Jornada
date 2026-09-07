-- Corpus exclusivamente sintético. Executar somente em JornadaPgCalibrationTest.
DO $$
BEGIN
    IF current_database() <> 'JornadaPgCalibrationTest' THEN
        RAISE EXCEPTION 'Fixture proibida fora do banco descartável de calibração.';
    END IF;
    IF (SELECT count(*) FROM identidade.modelo_linkage)
       + (SELECT count(*) FROM identidade.pessoa)
       + (SELECT count(*) FROM gold.pessoa)
       + (SELECT count(*) FROM silver.pessoa_observacao) <> 0 THEN
        RAISE EXCEPTION 'A fixture exige corpus vazio; nenhum dado preexistente será apagado.';
    END IF;
END $$;

-- CPFs válidos gerados a partir de bases artificiais; não são dados de cidadãos.
CREATE OR REPLACE FUNCTION pg_temp.cpf_sintetico(p_base BIGINT)
RETURNS CHAR(11) LANGUAGE plpgsql AS $$
DECLARE v_base TEXT := lpad(p_base::text,9,'0'); v_sum INTEGER; v_d1 INTEGER; v_d2 INTEGER; v_i INTEGER;
BEGIN
    v_sum := 0;
    FOR v_i IN 1..9 LOOP v_sum := v_sum + substring(v_base,v_i,1)::int*(11-v_i); END LOOP;
    v_d1 := (v_sum*10)%11; IF v_d1=10 THEN v_d1:=0; END IF;
    v_sum := 0;
    FOR v_i IN 1..9 LOOP v_sum := v_sum + substring(v_base,v_i,1)::int*(12-v_i); END LOOP;
    v_sum := v_sum + v_d1*2;
    v_d2 := (v_sum*10)%11; IF v_d2=10 THEN v_d2:=0; END IF;
    RETURN (v_base||v_d1::text||v_d2::text)::char(11);
END $$;

INSERT INTO ref.gestor(gestor_id,codigo,nome) VALUES
(9101,'CI_CAL_A','Gestor sintético A'),(9102,'CI_CAL_B','Gestor sintético B');
INSERT INTO ref.sistema_origem(sistema_origem_id,gestor_id,codigo,nome) VALUES
(9201,9101,'CI_CAL_A','Sistema sintético A'),(9202,9102,'CI_CAL_B','Sistema sintético B');
INSERT INTO ref.gestor_pessoa_versao(gestor_pessoa_versao_id,gestor_id,versao) VALUES
(9301,9101,1),(9302,9102,1);
INSERT INTO ingestao.entrega(entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,
    idempotency_key,payload_sha256,bytes_recebidos,status,data_referencia) VALUES
('91000000-0000-4000-8000-000000000001',9101,9201,9301,'ci-cal-a',repeat('a',64),0,'PROCESSADA','2026-01-01T00:00:00Z'),
('91000000-0000-4000-8000-000000000002',9102,9202,9302,'ci-cal-b',repeat('b',64),0,'PROCESSADA','2026-01-01T00:00:00Z');
INSERT INTO ingestao.lote(lote_id,entrega_id,lote_seq,lote_total,status) VALUES
('92000000-0000-4000-8000-000000000001','91000000-0000-4000-8000-000000000001',1,1,'PROCESSADO'),
('92000000-0000-4000-8000-000000000002','91000000-0000-4000-8000-000000000002',1,1,'PROCESSADO');

INSERT INTO identidade.pessoa(pessoa_uuid,status)
SELECT md5('pg-cal-person-'||i)::uuid,'ATIVO' FROM generate_series(1,10) AS i;
INSERT INTO gold.pessoa(pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia)
SELECT md5('pg-cal-person-'||i)::uuid,pg_temp.cpf_sintetico(100000000+i),'PRESENTE',
       'Pessoa Teste '||i,DATE '1980-04-10','Mae Teste '||i,CASE WHEN i<=8 THEN 2 ELSE 1 END,'BASELINE_FONTE_UNICA'
FROM generate_series(1,10) AS i;

INSERT INTO silver.pessoa_origem(pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem)
SELECT 94000+i*10+s,9200+s,'CI-'||i
FROM generate_series(1,10) AS i CROSS JOIN generate_series(1,2) AS s WHERE s=1 OR i<=8;
INSERT INTO silver.pessoa_observacao(
    pessoa_observacao_id,pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,
    conteudo_hash,cpf,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
SELECT 95000+i*10+s,94000+i*10+s,
       CASE WHEN s=1 THEN '92000000-0000-4000-8000-000000000001'::uuid ELSE '92000000-0000-4000-8000-000000000002'::uuid END,
       9100+s,'CI-'||i,1,encode(sha256(convert_to('pg-cal:'||i||':'||s,'UTF8')),'hex'),
       pg_temp.cpf_sintetico(100000000+i),
       'Pessoa Teste '||i||CASE WHEN s=2 AND i%2=0 THEN ' A' ELSE '' END,
       upper('Pessoa Teste '||i||CASE WHEN s=2 AND i%2=0 THEN ' A' ELSE '' END),
       DATE '1980-04-10'+CASE WHEN s=2 AND i%3=0 THEN 1 ELSE 0 END,
       'Mae Teste '||i,upper('Mae Teste '||i),'2026-01-01T00:00:00Z'::timestamptz+s*interval '1 second'
FROM generate_series(1,10) AS i CROSS JOIN generate_series(1,2) AS s WHERE s=1 OR i<=8;
INSERT INTO identidade.vinculo_fonte(
    vinculo_fonte_id,pessoa_observacao_id,pessoa_uuid,metodo_resolucao,status,ativo,resolvido_em)
SELECT 96000+i*10+s,95000+i*10+s,md5('pg-cal-person-'||i)::uuid,'CPF_DETERMINISTICO','RESOLVIDO',TRUE,CURRENT_TIMESTAMP
FROM generate_series(1,10) AS i CROSS JOIN generate_series(1,2) AS s WHERE s=1 OR i<=8;

CREATE TABLE controle.calibracao_ci_fixture(fixture_id UUID PRIMARY KEY,expected_people INTEGER NOT NULL);
INSERT INTO controle.calibracao_ci_fixture VALUES('93000000-0000-4000-8000-000000000001',10);
