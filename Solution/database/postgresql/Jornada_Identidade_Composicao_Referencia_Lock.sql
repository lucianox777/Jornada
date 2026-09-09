-- Serialização entre writers determinísticos e composição governada.
-- Ordem de locks: origem primeiro, referência depois, igual ao leitor de composição.
BEGIN;
DO $$
BEGIN
 IF to_regprocedure('identidade.publicar_referencia_progressiva_deterministica(bigint,uuid,character varying,character varying)') IS NULL THEN
  RAISE EXCEPTION 'Persistência progressiva determinística não instalada.';
 END IF;
END $$;

CREATE OR REPLACE FUNCTION identidade.publicar_referencia_progressiva_deterministica(
 p_source BIGINT,
 p_canonical UUID,
 p_evidencia VARCHAR(255),
 p_politica VARCHAR(120))
RETURNS BIGINT LANGUAGE plpgsql AS $$
DECLARE
 v_initial UUID;
 v_current UUID;
 v_estado VARCHAR(20);
 v_version BIGINT;
 v_new_version BIGINT;
 v_now TIMESTAMPTZ;
 v_external UUID;
BEGIN
 IF p_source IS NULL OR p_source<=0 THEN RAISE EXCEPTION 'Origem inválida.'; END IF;
 IF p_canonical IS NULL OR p_canonical='00000000-0000-0000-0000-000000000000'::uuid THEN RAISE EXCEPTION 'UUID canônico inválido.'; END IF;
 IF p_evidencia IS NULL OR btrim(p_evidencia)='' OR p_politica IS NULL OR btrim(p_politica)='' THEN
  RAISE EXCEPTION 'Publicação de referência exige evidência e política.';
 END IF;

 PERFORM identidade.assegurar_origem_progressiva(p_source);
 SELECT initial_uuid,canonical_uuid,estado,versao INTO v_initial,v_current,v_estado,v_version
   FROM identidade.pessoa_origem_progressiva
  WHERE pessoa_origem_id=p_source
  FOR UPDATE;

 PERFORM pg_advisory_xact_lock(hashtextextended(
  'JORNADA:COMPOSICAO:REF:' || lower(p_canonical::text),0));

 IF v_estado='REFERENCIA' AND v_current=p_canonical THEN RETURN v_version; END IF;
 IF v_estado='REFERENCIA' AND v_current IS DISTINCT FROM p_canonical THEN
  RAISE EXCEPTION 'Referência progressiva já aponta outro UUID; correção governada necessária.';
 END IF;
 PERFORM 1 FROM identidade.pessoa WHERE pessoa_uuid=p_canonical;
 IF NOT FOUND THEN RAISE EXCEPTION 'UUID canônico inexistente.'; END IF;
 v_new_version:=v_version+1;
 v_now:=CURRENT_TIMESTAMP;
 v_external:=CASE WHEN p_canonical=v_initial THEN NULL ELSE p_canonical END;
 INSERT INTO identidade.pessoa_origem_progressiva_evento(
  evento_id,pessoa_origem_id,versao,tipo,estado,canonical_uuid,expected_version,resultado,target_uuid,
  evidencia_referencia,politica_versao,modelo_versao,universo_referencia,completo,ocorrido_em)
 VALUES(
  gen_random_uuid(),p_source,v_new_version,'RESOLUCAO','REFERENCIA',p_canonical,v_version,'ASSOCIACAO_EXISTENTE',p_canonical,
  p_evidencia,p_politica,NULL,NULL,TRUE,v_now);
 UPDATE identidade.pessoa_origem_progressiva
    SET canonical_uuid=p_canonical,estado='REFERENCIA',versao=v_new_version,
        ultima_resolucao_em=v_now,ultimo_destino_externo_uuid=v_external,atualizado_em=v_now
  WHERE pessoa_origem_id=p_source;
 RETURN v_new_version;
END $$;
COMMIT;
