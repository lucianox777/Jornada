-- PostgreSQL Linkage: evidência de calibração e validação explícita de rascunhos.
-- Instalar após Jornada_Linkage_Model_Core.sql. Não ativa modelos nem publica identidades.
-- O acesso de escrita a estas tabelas deve ser restrito às identidades técnicas autorizadas.
CREATE TABLE IF NOT EXISTS identidade.calibracao_linkage(
    modelo_id UUID PRIMARY KEY REFERENCES identidade.modelo_linkage(modelo_id),
    metodo_amostragem VARCHAR(80) NOT NULL,
    snapshot_token VARCHAR(200) NOT NULL,
    snapshot_sha256 CHAR(64) NOT NULL CHECK(snapshot_sha256 ~ '^[0-9a-f]{64}$'),
    amostra_m_sha256 CHAR(64) NOT NULL CHECK(amostra_m_sha256 ~ '^[0-9a-f]{64}$'),
    amostra_u_sha256 CHAR(64) NOT NULL CHECK(amostra_u_sha256 ~ '^[0-9a-f]{64}$'),
    parametros_sha256 CHAR(64) NOT NULL CHECK(parametros_sha256 ~ '^[0-9a-f]{64}$'),
    amostra_minima_m INTEGER NOT NULL CHECK(amostra_minima_m > 0),
    sintetico BOOLEAN NOT NULL DEFAULT FALSE,
    capturado_em TIMESTAMPTZ NOT NULL,
    validado_em TIMESTAMPTZ NULL,
    validacao_referencia VARCHAR(200) NULL,
    CHECK ((validado_em IS NULL AND validacao_referencia IS NULL)
        OR (validado_em IS NOT NULL AND validacao_referencia IS NOT NULL))
);

-- Manifesto de replay: congela versões de Calibrador/projeção/catálogos/plano e os
-- fingerprints dos dados efetivamente consumidos. Não contém PII nem valores dos pares M/U.
CREATE TABLE IF NOT EXISTS identidade.calibracao_replay_manifest(
    modelo_id UUID PRIMARY KEY REFERENCES identidade.modelo_linkage(modelo_id),
    manifest_version VARCHAR(80) NOT NULL,
    calibrator_version VARCHAR(120) NOT NULL,
    projection_schema_version VARCHAR(120) NOT NULL,
    projection_fingerprint CHAR(64) NOT NULL CHECK(projection_fingerprint ~ '^[0-9a-f]{64}$'),
    algorithm_catalog_version VARCHAR(120) NOT NULL,
    comparator_catalog_version VARCHAR(120) NOT NULL,
    blocking_plan_version VARCHAR(120) NOT NULL,
    blocking_plan_fingerprint CHAR(64) NOT NULL CHECK(blocking_plan_fingerprint ~ '^[0-9a-f]{64}$'),
    person_source_id VARCHAR(200) NOT NULL,
    person_source_version VARCHAR(200) NOT NULL,
    person_content_fingerprint CHAR(64) NOT NULL CHECK(person_content_fingerprint ~ '^[0-9a-f]{64}$'),
    training_source_id VARCHAR(200) NOT NULL,
    training_source_version VARCHAR(200) NOT NULL,
    training_content_fingerprint CHAR(64) NOT NULL CHECK(training_content_fingerprint ~ '^[0-9a-f]{64}$'),
    external_snapshots_json JSONB NOT NULL DEFAULT '[]'::jsonb CHECK(jsonb_typeof(external_snapshots_json)='array'),
    manifest_fingerprint CHAR(64) NOT NULL CHECK(manifest_fingerprint ~ '^[0-9a-f]{64}$')
);

-- Reutiliza a trava do cabeçalho: nenhuma evidência pode mudar após VALIDADO.
DROP TRIGGER IF EXISTS tr_pg_linkage_evidencia_editavel ON identidade.calibracao_linkage;
CREATE TRIGGER tr_pg_linkage_evidencia_editavel
BEFORE INSERT OR UPDATE OR DELETE ON identidade.calibracao_linkage
FOR EACH ROW EXECUTE FUNCTION identidade.fn_linkage_evidencia_editavel();

DROP TRIGGER IF EXISTS tr_pg_linkage_replay_editavel ON identidade.calibracao_replay_manifest;
CREATE TRIGGER tr_pg_linkage_replay_editavel
BEFORE INSERT OR UPDATE OR DELETE ON identidade.calibracao_replay_manifest
FOR EACH ROW EXECUTE FUNCTION identidade.fn_linkage_evidencia_editavel();

CREATE OR REPLACE FUNCTION identidade.validar_modelo_linkage_pg(p_versao INTEGER)
RETURNS UUID LANGUAGE plpgsql AS $$
DECLARE
    v_modelo identidade.modelo_linkage%ROWTYPE;
    v_evidencia identidade.calibracao_linkage%ROWTYPE;
    v_hash TEXT;
    v_required TEXT[] := ARRAY[
        'M_NOME_EXACT','M_NOME_HIGH','M_NOME_MEDIUM','M_NOME_LOW',
        'U_NOME_EXACT','U_NOME_HIGH','U_NOME_MEDIUM','U_NOME_LOW',
        'M_NOME_MAE_EXACT','M_NOME_MAE_HIGH','M_NOME_MAE_MEDIUM','M_NOME_MAE_LOW',
        'U_NOME_MAE_EXACT','U_NOME_MAE_HIGH','U_NOME_MAE_MEDIUM','U_NOME_MAE_LOW',
        'M_NASC_DIA_EXACT','M_NASC_DIA_DIFF','U_NASC_DIA_EXACT','U_NASC_DIA_DIFF',
        'M_NASC_MES_EXACT','M_NASC_MES_DIFF','U_NASC_MES_EXACT','U_NASC_MES_DIFF',
        'M_NASC_ANO_EXACT','M_NASC_ANO_DIFF','U_NASC_ANO_EXACT','U_NASC_ANO_DIFF',
        'M_DATA_NASCIMENTO_EXACT','U_DATA_NASCIMENTO_EXACT',
        'PRIOR_MATCH_PROBABILITY','PRIOR_BLOCK_MIN','PRIOR_BLOCK_MAX',
        'T_LINKAGE','CONFLICT_MARGIN','SMOOTHING_ALPHA','M_SAMPLE_SIZE','U_SAMPLE_SIZE',
        'POPULATION_SIZE','POPULATION_WITH_CPF','DISTINCT_BIRTH_DATE',
        'TRAINING_SAMPLE_POOL_SIZE','MIN_M_INDEPENDENT_PAIRS',
        'SCORING_BIRTH_COMPONENTS_V2'];
    v_prefix TEXT;
    v_sum NUMERIC;
    v_count BIGINT;
BEGIN
    -- A mesma row lock é usada por todos os triggers de evidência.
    SELECT * INTO v_modelo FROM identidade.modelo_linkage
      WHERE versao=p_versao FOR UPDATE;
    IF NOT FOUND OR v_modelo.status <> 'RASCUNHO' THEN
        RAISE EXCEPTION 'Somente uma versão RASCUNHO existente pode ser validada.';
    END IF;
    IF v_modelo.algoritmo_versao <> 'FELLEGI_SUNTER_BIRTH_COMPONENTS_V2'
       OR v_modelo.normalizacao_versao <> 'IDENTITY_NORMALIZATION_V1' THEN
        RAISE EXCEPTION 'Versão de algoritmo ou normalização incompatível com este calibrador.';
    END IF;
    SELECT * INTO v_evidencia FROM identidade.calibracao_linkage
      WHERE modelo_id=v_modelo.modelo_id;
    IF NOT FOUND OR v_evidencia.validado_em IS NOT NULL THEN
        RAISE EXCEPTION 'Evidência de calibração ausente ou já finalizada.';
    END IF;
    IF NOT EXISTS (
        SELECT 1
          FROM identidade.calibracao_replay_manifest rm
          JOIN identidade.linkage_ruleset rs ON rs.modelo_id=rm.modelo_id
         WHERE rm.modelo_id=v_modelo.modelo_id
           AND rm.blocking_plan_version=rs.ruleset_versao
           AND rm.blocking_plan_fingerprint=rs.fingerprint_sha256
           AND rm.manifest_version='CALIBRATION_REPLAY_MANIFEST_V1'
    ) THEN
        RAISE EXCEPTION 'Manifesto de replay ausente ou divergente do ruleset persistido.';
    END IF;
    IF v_evidencia.sintetico AND
       (current_database() <> 'JornadaPgCalibrationTest'
        OR v_modelo.base_referencia <> 'CI_LINKAGE_SYNTHETIC') THEN
        RAISE EXCEPTION 'Evidência sintética somente é permitida no banco descartável de regressão.';
    END IF;
    IF NOT v_evidencia.sintetico AND v_evidencia.amostra_minima_m < 100 THEN
        RAISE EXCEPTION 'Amostra mínima independente inferior ao limite técnico.';
    END IF;
    IF v_modelo.pessoas_unicas IS NULL OR v_modelo.pessoas_unicas <= 0
       OR v_modelo.registros_lidos IS DISTINCT FROM v_modelo.pessoas_unicas
       OR v_modelo.amostra_m_tamanho IS NULL OR v_modelo.amostra_u_tamanho IS NULL
       OR v_modelo.amostra_m_tamanho < v_evidencia.amostra_minima_m
       OR v_modelo.amostra_u_tamanho <= 0
       OR v_modelo.snapshot_referencia IS NULL OR v_modelo.snapshot_capturado_em IS NULL THEN
        RAISE EXCEPTION 'População, snapshot ou amostras de calibração inválidos.';
    END IF;
    IF EXISTS (
        SELECT nome FROM unnest(v_required) AS req(nome)
        EXCEPT SELECT nome FROM identidade.parametro_linkage WHERE modelo_id=v_modelo.modelo_id
    ) THEN
        RAISE EXCEPTION 'Modelo probabilístico incompleto: parâmetros obrigatórios ausentes.';
    END IF;
    SELECT encode(sha256(convert_to(
        string_agg(nome || '=' || valor::text, E'\n' ORDER BY nome COLLATE "C") || E'\n', 'UTF8')), 'hex')
      INTO v_hash FROM identidade.parametro_linkage WHERE modelo_id=v_modelo.modelo_id;
    IF v_hash IS DISTINCT FROM v_evidencia.parametros_sha256 THEN
        RAISE EXCEPTION 'Fingerprint dos parâmetros diverge da evidência capturada.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM identidade.parametro_linkage
         WHERE modelo_id=v_modelo.modelo_id AND (
            ((left(nome,2) IN ('M_','U_') AND nome NOT IN ('M_SAMPLE_SIZE','U_SAMPLE_SIZE'))
                OR nome IN ('PRIOR_MATCH_PROBABILITY','PRIOR_BLOCK_MIN','PRIOR_BLOCK_MAX'))
                AND (valor <= 0 OR valor >= 1)
            OR nome='T_LINKAGE' AND (valor < 0.5 OR valor > 0.999999)
            OR nome='CONFLICT_MARGIN' AND (valor <= 0 OR valor > 0.5)
            OR nome='SMOOTHING_ALPHA' AND valor <= 0
        )
    ) THEN RAISE EXCEPTION 'Parâmetros fora do domínio probabilístico.'; END IF;
    IF (SELECT valor FROM identidade.parametro_linkage WHERE modelo_id=v_modelo.modelo_id AND nome='PRIOR_BLOCK_MIN') >
       (SELECT valor FROM identidade.parametro_linkage WHERE modelo_id=v_modelo.modelo_id AND nome='PRIOR_BLOCK_MAX') THEN
        RAISE EXCEPTION 'Limites do prior invertidos.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM (VALUES
          ('M_SAMPLE_SIZE',v_modelo.amostra_m_tamanho::NUMERIC),
          ('U_SAMPLE_SIZE',v_modelo.amostra_u_tamanho::NUMERIC),
          ('POPULATION_SIZE',v_modelo.pessoas_unicas::NUMERIC),
          ('MIN_M_INDEPENDENT_PAIRS',v_evidencia.amostra_minima_m::NUMERIC),
          ('SCORING_BIRTH_COMPONENTS_V2',1::NUMERIC)
        ) AS expected(nome,valor)
        JOIN identidade.parametro_linkage p ON p.modelo_id=v_modelo.modelo_id AND p.nome=expected.nome
        WHERE p.valor <> expected.valor
    ) OR (SELECT valor FROM identidade.parametro_linkage WHERE modelo_id=v_modelo.modelo_id AND nome='POPULATION_WITH_CPF') > v_modelo.pessoas_unicas
      OR (SELECT valor FROM identidade.parametro_linkage WHERE modelo_id=v_modelo.modelo_id AND nome='DISTINCT_BIRTH_DATE') NOT BETWEEN 1 AND v_modelo.pessoas_unicas THEN
        RAISE EXCEPTION 'Parâmetros de população, amostra ou scoring inconsistentes.';
    END IF;
    FOR v_prefix IN SELECT unnest(ARRAY['M_NOME','U_NOME','M_NOME_MAE','U_NOME_MAE']) LOOP
        SELECT count(*),COALESCE(sum(valor),0) INTO v_count,v_sum
          FROM identidade.parametro_linkage WHERE modelo_id=v_modelo.modelo_id
           AND nome=ANY(ARRAY[v_prefix||'_EXACT',v_prefix||'_HIGH',v_prefix||'_MEDIUM',v_prefix||'_LOW']);
        IF v_count<>4 OR abs(v_sum-1)>0.00000000001 THEN
            RAISE EXCEPTION 'Distribuição de nomes incompleta ou não normalizada: %.',v_prefix;
        END IF;
    END LOOP;
    FOR v_prefix IN SELECT unnest(ARRAY['M_NASC_DIA','U_NASC_DIA','M_NASC_MES','U_NASC_MES','M_NASC_ANO','U_NASC_ANO']) LOOP
        SELECT count(*),COALESCE(sum(valor),0) INTO v_count,v_sum
          FROM identidade.parametro_linkage WHERE modelo_id=v_modelo.modelo_id
           AND nome=ANY(ARRAY[v_prefix||'_EXACT',v_prefix||'_DIFF']);
        IF v_count<>2 OR abs(v_sum-1)>0.00000000001 THEN
            RAISE EXCEPTION 'Distribuição de nascimento incompleta ou não normalizada: %.',v_prefix;
        END IF;
    END LOOP;
    IF NOT EXISTS (SELECT 1 FROM identidade.estatistica_linkage
        WHERE modelo_id=v_modelo.modelo_id AND nome='POPULATION_SIZE' AND valor=v_modelo.pessoas_unicas) THEN
        RAISE EXCEPTION 'Estatística populacional ausente ou inconsistente.';
    END IF;
    FOR v_prefix IN SELECT unnest(ARRAY['NASC_DIA','NASC_MES','NASC_ANO']) LOOP
        SELECT count(*),COALESCE(sum(ocorrencias),0) INTO v_count,v_sum
          FROM identidade.frequencia_linkage WHERE modelo_id=v_modelo.modelo_id AND atributo=v_prefix;
        IF v_count=0 OR v_sum<>v_modelo.pessoas_unicas THEN
            RAISE EXCEPTION 'Frequência populacional incompleta: %.',v_prefix;
        END IF;
    END LOOP;
    IF EXISTS (SELECT 1 FROM identidade.frequencia_linkage WHERE modelo_id=v_modelo.modelo_id
        AND populacao_referencia<>v_modelo.pessoas_unicas) THEN
        RAISE EXCEPTION 'Denominador de frequência inconsistente.';
    END IF;
    -- A evidência e o cabeçalho tornam-se imutáveis no mesmo commit.
    UPDATE identidade.calibracao_linkage SET validado_em=CURRENT_TIMESTAMP,
        validacao_referencia='PG_CALIBRATION_VALIDATION_V1'
      WHERE modelo_id=v_modelo.modelo_id;
    UPDATE identidade.modelo_linkage SET status='VALIDADO' WHERE modelo_id=v_modelo.modelo_id;
    RETURN v_modelo.modelo_id;
END $$;
