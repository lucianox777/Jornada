\set ON_ERROR_STOP on

DO $$
DECLARE
 v_sistema BIGINT;
 v_lote UUID;
 v_gestor BIGINT;
 v_source BIGINT;
 v_obs BIGINT;
 v_uuid UUID;
BEGIN
 SELECT o.sistema_origem_id,po.lote_id,po.gestor_id
   INTO v_sistema,v_lote,v_gestor
   FROM silver.pessoa_observacao po
   JOIN silver.pessoa_origem o ON o.pessoa_origem_id=po.pessoa_origem_id
  ORDER BY po.pessoa_observacao_id
  LIMIT 1;
 IF v_sistema IS NULL OR v_lote IS NULL OR v_gestor IS NULL THEN
  RAISE EXCEPTION 'Fixture base insuficiente para smoke de cutover.';
 END IF;
 IF EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE codigo_pessoa_origem='CUTOVER-COMMIT-20260908') THEN
  RAISE EXCEPTION 'Smoke requer banco limpo.';
 END IF;

 INSERT INTO silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem)
 VALUES(v_sistema,'CUTOVER-COMMIT-20260908') RETURNING pessoa_origem_id INTO v_source;
 INSERT INTO silver.pessoa_observacao(
  pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
  cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
 VALUES(v_source,v_lote,v_gestor,'CUTOVER-COMMIT-20260908',1,repeat('a',64),NULL,'SEM_CPF',
        'PESSOA CUTOVER','PESSOA CUTOVER',DATE '1990-01-01','MAE CUTOVER','MAE CUTOVER',CURRENT_TIMESTAMP)
 RETURNING pessoa_observacao_id INTO v_obs;
 INSERT INTO identidade.vinculo_fonte(
  pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
 VALUES(v_obs,NULL,'PENDENTE_PROBABILISTICO',NULL,'NAO_RESOLVIDO',NULL,TRUE,NULL,'AGUARDA_LINKAGE_SOB_DEMANDA');
 SELECT initial_uuid INTO v_uuid FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=v_source;
 IF v_uuid IS NULL THEN RAISE EXCEPTION 'Novo vínculo não materializou initial_uuid.'; END IF;
 IF (SELECT count(*) FROM identidade.pessoa_origem_progressiva_evento WHERE pessoa_origem_id=v_source AND tipo='CRIACAO')<>1 THEN
  RAISE EXCEPTION 'Criação inicial não produziu exatamente um evento.';
 END IF;
END $$;

DO $$
DECLARE
 v_source BIGINT;
 v_lote UUID;
 v_gestor BIGINT;
 v_obs BIGINT;
 v_before UUID;
 v_after UUID;
BEGIN
 SELECT o.pessoa_origem_id,po.lote_id,po.gestor_id,p.initial_uuid
   INTO v_source,v_lote,v_gestor,v_before
   FROM silver.pessoa_origem o
   JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id
   JOIN silver.pessoa_observacao po ON po.pessoa_origem_id=o.pessoa_origem_id
  WHERE o.codigo_pessoa_origem='CUTOVER-COMMIT-20260908'
  ORDER BY po.pessoa_observacao_id LIMIT 1;
 INSERT INTO silver.pessoa_observacao(
  pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
  cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
 VALUES(v_source,v_lote,v_gestor,'CUTOVER-COMMIT-20260908',2,repeat('b',64),NULL,'SEM_CPF',
        'PESSOA CUTOVER','PESSOA CUTOVER',DATE '1990-01-01','MAE CUTOVER','MAE CUTOVER',CURRENT_TIMESTAMP)
 RETURNING pessoa_observacao_id INTO v_obs;
 INSERT INTO identidade.vinculo_fonte(
  pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
 VALUES(v_obs,NULL,'PENDENTE_PROBABILISTICO',NULL,'NAO_RESOLVIDO',NULL,TRUE,NULL,'AGUARDA_LINKAGE_SOB_DEMANDA');
 SELECT initial_uuid INTO v_after FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=v_source;
 IF v_after IS DISTINCT FROM v_before THEN RAISE EXCEPTION 'Nova versão alterou initial_uuid.'; END IF;
 IF (SELECT count(*) FROM identidade.pessoa_origem_progressiva_evento WHERE pessoa_origem_id=v_source AND tipo='CRIACAO')<>1 THEN
  RAISE EXCEPTION 'Nova versão duplicou evento de criação.';
 END IF;
END $$;

-- CPF determinístico publica REFERENCIA; replay idempotente não cria nova resolução.
DO $$
DECLARE
 v_source BIGINT;
 v_lote UUID;
 v_gestor BIGINT;
 v_obs BIGINT;
 v_canonical UUID:=gen_random_uuid();
 v_estado VARCHAR(20);
 v_canonical_after UUID;
 v_version BIGINT;
BEGIN
 SELECT o.pessoa_origem_id,po.lote_id,po.gestor_id
   INTO v_source,v_lote,v_gestor
   FROM silver.pessoa_origem o
   JOIN silver.pessoa_observacao po ON po.pessoa_origem_id=o.pessoa_origem_id
  WHERE o.codigo_pessoa_origem='CUTOVER-COMMIT-20260908'
  ORDER BY po.pessoa_observacao_id LIMIT 1;
 INSERT INTO identidade.pessoa(pessoa_uuid,status) VALUES(v_canonical,'ATIVO');
 INSERT INTO silver.pessoa_observacao(
  pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
  cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
 VALUES(v_source,v_lote,v_gestor,'CUTOVER-COMMIT-20260908',3,repeat('d',64),'52998224725',NULL,
        'PESSOA CUTOVER','PESSOA CUTOVER',DATE '1990-01-01','MAE CUTOVER','MAE CUTOVER',CURRENT_TIMESTAMP)
 RETURNING pessoa_observacao_id INTO v_obs;
 INSERT INTO identidade.vinculo_fonte(
  pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
 VALUES(v_obs,v_canonical,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,TRUE,CURRENT_TIMESTAMP,'CPF_DETERMINISTICO');
 SELECT estado,canonical_uuid,versao INTO v_estado,v_canonical_after,v_version
   FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=v_source;
 IF v_estado<>'REFERENCIA' OR v_canonical_after IS DISTINCT FROM v_canonical OR v_version<>1 THEN
  RAISE EXCEPTION 'CPF determinístico não publicou REFERENCIA versão 1.';
 END IF;
 IF (SELECT count(*) FROM identidade.pessoa_origem_progressiva_evento
     WHERE pessoa_origem_id=v_source AND tipo='RESOLUCAO' AND versao=1 AND estado='REFERENCIA'
       AND canonical_uuid=v_canonical AND target_uuid=v_canonical AND resultado='ASSOCIACAO_EXISTENTE'
       AND evidencia_referencia='CPF_ANCORA_DETERMINISTICA' AND politica_versao='CPF_ANCORA_V1' AND completo)<>1 THEN
  RAISE EXCEPTION 'REFERENCIA não produziu recibo determinístico esperado.';
 END IF;

 INSERT INTO silver.pessoa_observacao(
  pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
  cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
 VALUES(v_source,v_lote,v_gestor,'CUTOVER-COMMIT-20260908',4,repeat('e',64),'52998224725',NULL,
        'PESSOA CUTOVER','PESSOA CUTOVER',DATE '1990-01-01','MAE CUTOVER','MAE CUTOVER',CURRENT_TIMESTAMP)
 RETURNING pessoa_observacao_id INTO v_obs;
 INSERT INTO identidade.vinculo_fonte(
  pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
 VALUES(v_obs,v_canonical,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,TRUE,CURRENT_TIMESTAMP,'CPF_DETERMINISTICO_REPLAY');
 SELECT estado,canonical_uuid,versao INTO v_estado,v_canonical_after,v_version
   FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=v_source;
 IF v_estado<>'REFERENCIA' OR v_canonical_after IS DISTINCT FROM v_canonical OR v_version<>1 THEN
  RAISE EXCEPTION 'Replay determinístico alterou a referência.';
 END IF;
 IF (SELECT count(*) FROM identidade.pessoa_origem_progressiva_evento WHERE pessoa_origem_id=v_source AND tipo='RESOLUCAO')<>1 THEN
  RAISE EXCEPTION 'Replay determinístico duplicou recibo de resolução.';
 END IF;
END $$;

-- A mudança de UUID canônico falha dentro de subtransação e não deixa resíduos.
DO $$
DECLARE
 v_source BIGINT;
 v_lote UUID;
 v_gestor BIGINT;
 v_obs BIGINT;
 v_canonical UUID;
 v_conflicting UUID:=gen_random_uuid();
 v_version BIGINT;
 v_error TEXT;
BEGIN
 SELECT o.pessoa_origem_id,po.lote_id,po.gestor_id,p.canonical_uuid,p.versao
   INTO v_source,v_lote,v_gestor,v_canonical,v_version
   FROM silver.pessoa_origem o
   JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id
   JOIN silver.pessoa_observacao po ON po.pessoa_origem_id=o.pessoa_origem_id
  WHERE o.codigo_pessoa_origem='CUTOVER-COMMIT-20260908'
  ORDER BY po.pessoa_observacao_id LIMIT 1;
 BEGIN
  INSERT INTO identidade.pessoa(pessoa_uuid,status) VALUES(v_conflicting,'ATIVO');
  INSERT INTO silver.pessoa_observacao(
   pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
   cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
  VALUES(v_source,v_lote,v_gestor,'CUTOVER-COMMIT-20260908',5,repeat('f',64),'52998224725',NULL,
         'PESSOA CUTOVER','PESSOA CUTOVER',DATE '1990-01-01','MAE CUTOVER','MAE CUTOVER',CURRENT_TIMESTAMP)
  RETURNING pessoa_observacao_id INTO v_obs;
  INSERT INTO identidade.vinculo_fonte(
   pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
  VALUES(v_obs,v_conflicting,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,TRUE,CURRENT_TIMESTAMP,'CPF_DETERMINISTICO_DIVERGENTE');
  RAISE EXCEPTION 'Transferência indevida de REFERENCIA foi aceita.';
 EXCEPTION WHEN OTHERS THEN
  GET STACKED DIAGNOSTICS v_error=MESSAGE_TEXT;
  IF v_error NOT LIKE 'Referência progressiva já aponta outro UUID%' THEN
   RAISE;
  END IF;
 END;
 IF EXISTS(SELECT 1 FROM identidade.pessoa WHERE pessoa_uuid=v_conflicting) THEN
  RAISE EXCEPTION 'Falha fechada deixou Pessoa conflitante.';
 END IF;
 IF NOT EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva
               WHERE pessoa_origem_id=v_source AND estado='REFERENCIA' AND canonical_uuid=v_canonical AND versao=v_version) THEN
  RAISE EXCEPTION 'Falha fechada alterou a REFERENCIA consolidada.';
 END IF;
 IF (SELECT count(*) FROM identidade.pessoa_origem_progressiva_evento WHERE pessoa_origem_id=v_source AND tipo='RESOLUCAO')<>1 THEN
  RAISE EXCEPTION 'Falha fechada deixou recibo espúrio.';
 END IF;
END $$;

CREATE TEMP TABLE cutover_baseline(pessoas BIGINT NOT NULL);
INSERT INTO cutover_baseline SELECT count(*) FROM identidade.pessoa;
BEGIN;
DO $$
DECLARE
 v_sistema BIGINT;
 v_lote UUID;
 v_gestor BIGINT;
 v_source BIGINT;
 v_obs BIGINT;
 v_uuid UUID;
BEGIN
 SELECT o.sistema_origem_id,po.lote_id,po.gestor_id
   INTO v_sistema,v_lote,v_gestor
   FROM silver.pessoa_observacao po
   JOIN silver.pessoa_origem o ON o.pessoa_origem_id=po.pessoa_origem_id
  ORDER BY po.pessoa_observacao_id LIMIT 1;
 INSERT INTO silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem)
 VALUES(v_sistema,'CUTOVER-ROLLBACK-20260908') RETURNING pessoa_origem_id INTO v_source;
 INSERT INTO silver.pessoa_observacao(
  pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
  cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
 VALUES(v_source,v_lote,v_gestor,'CUTOVER-ROLLBACK-20260908',1,repeat('c',64),NULL,'SEM_CPF',
        'PESSOA ROLLBACK','PESSOA ROLLBACK',DATE '1991-01-01','MAE ROLLBACK','MAE ROLLBACK',CURRENT_TIMESTAMP)
 RETURNING pessoa_observacao_id INTO v_obs;
 INSERT INTO identidade.vinculo_fonte(
  pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
 VALUES(v_obs,NULL,'PENDENTE_PROBABILISTICO',NULL,'NAO_RESOLVIDO',NULL,TRUE,NULL,'AGUARDA_LINKAGE_SOB_DEMANDA');
 SELECT initial_uuid INTO v_uuid FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=v_source;
 IF v_uuid IS NULL THEN RAISE EXCEPTION 'Fixture rollback não materializou initial_uuid antes do abort.'; END IF;
END $$;
ROLLBACK;

DO $$
BEGIN
 IF EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE codigo_pessoa_origem='CUTOVER-ROLLBACK-20260908') THEN
  RAISE EXCEPTION 'Rollback deixou origem Silver.';
 END IF;
 IF (SELECT count(*) FROM identidade.pessoa)<>(SELECT pessoas FROM cutover_baseline) THEN
  RAISE EXCEPTION 'Rollback deixou Pessoa órfã.';
 END IF;
END $$;
DROP TABLE cutover_baseline;

SELECT 'PROGRESSIVE PROCESSOR CUTOVER POSTGRESQL: OK';
