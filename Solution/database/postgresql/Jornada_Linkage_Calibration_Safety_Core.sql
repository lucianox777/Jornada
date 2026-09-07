-- Guarda de promoção para a primeira calibração MVCC PostgreSQL.
-- Instalar depois de Jornada_Linkage_Calibration_Core.sql, inclusive em upgrades.
-- Não altera modelos de outros métodos, pesos ou parâmetros canônicos.
CREATE OR REPLACE FUNCTION identidade.fn_linkage_calibracao_estado()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE v_evidencia identidade.calibracao_linkage%ROWTYPE;
BEGIN
    -- Uma versão piloto não pode ser reclassificada para contornar o bloqueio.
    IF TG_OP='UPDATE' THEN
        IF OLD.amostra_metodo='M_INTERGESTOR_U_GOLD_MVCC_V2'
           AND NEW.amostra_metodo IS DISTINCT FROM OLD.amostra_metodo THEN
            RAISE EXCEPTION 'Não é permitido reclassificar o método de um modelo piloto.';
        END IF;
    END IF;
    IF NEW.amostra_metodo IS DISTINCT FROM 'M_INTERGESTOR_U_GOLD_MVCC_V2' THEN
        RETURN NEW;
    END IF;
    IF TG_OP='UPDATE' THEN
        IF NEW.status IS NOT DISTINCT FROM OLD.status THEN RETURN NEW; END IF;
    END IF;
    IF NEW.status IN('ATIVO','INATIVO') THEN
        RAISE EXCEPTION 'O método de calibração u por nascimento exato não está habilitado para ativação.';
    END IF;
    IF NEW.status='VALIDADO' THEN
        SELECT * INTO v_evidencia FROM identidade.calibracao_linkage
         WHERE modelo_id=NEW.modelo_id;
        IF NOT FOUND OR v_evidencia.validado_em IS NULL
           OR v_evidencia.metodo_amostragem IS DISTINCT FROM NEW.amostra_metodo
           OR NOT v_evidencia.sintetico
           OR current_database()<>'JornadaPgCalibrationTest'
           OR NEW.base_referencia<>'CI_LINKAGE_SYNTHETIC' THEN
            RAISE EXCEPTION 'Validação deste método piloto é restrita ao corpus sintético descartável.';
        END IF;
    END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS tr_pg_linkage_calibracao_estado ON identidade.modelo_linkage;
CREATE TRIGGER tr_pg_linkage_calibracao_estado
BEFORE INSERT OR UPDATE ON identidade.modelo_linkage
FOR EACH ROW EXECUTE FUNCTION identidade.fn_linkage_calibracao_estado();
