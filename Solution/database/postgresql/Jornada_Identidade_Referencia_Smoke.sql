-- Executar antes da migração V2, em banco descartável com V1 instalado.
BEGIN;
DO $$
DECLARE v_source BIGINT; v_uuid UUID; v_now TIMESTAMPTZ:=CURRENT_TIMESTAMP;
BEGIN
 SELECT pessoa_origem_id INTO v_source FROM silver.pessoa_origem ORDER BY pessoa_origem_id LIMIT 1;
 IF v_source IS NULL THEN RAISE EXCEPTION 'Fixture Silver ausente.'; END IF;
 v_uuid:=identidade.assegurar_origem_progressiva(v_source);
 IF EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=v_source AND versao<>0) THEN
  RAISE EXCEPTION 'Fixture exige origem progressiva inicial.';
 END IF;
 INSERT INTO identidade.pessoa_origem_progressiva_evento(evento_id,pessoa_origem_id,versao,tipo,estado,canonical_uuid,expected_version,resultado,evidencia_referencia,politica_versao,universo_referencia,completo,ocorrido_em)
 VALUES(gen_random_uuid(),v_source,1,'RESOLUCAO','RESOLVIDA',v_uuid,0,'NOVA_IDENTIDADE','synthetic:reference-upgrade','TEST_V1','synthetic:complete',TRUE,v_now);
 UPDATE identidade.pessoa_origem_progressiva SET estado='RESOLVIDA',canonical_uuid=v_uuid,versao=1,ultima_resolucao_em=v_now,atualizado_em=v_now WHERE pessoa_origem_id=v_source;
END $$;
COMMIT;
