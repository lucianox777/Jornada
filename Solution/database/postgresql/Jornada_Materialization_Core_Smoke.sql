\set ON_ERROR_STOP on

BEGIN;

DO $$
BEGIN
    IF to_regclass('silver.pessoa_origem') IS NULL
       OR to_regclass('silver.pessoa_observacao') IS NULL
       OR to_regclass('silver.registro_origem') IS NULL
       OR to_regclass('silver.registro_observacao') IS NULL
       OR to_regclass('identidade.pessoa') IS NULL
       OR to_regclass('identidade.identity_map') IS NULL
       OR to_regclass('identidade.vinculo_fonte') IS NULL
       OR to_regclass('gold.pessoa') IS NULL
       OR to_regclass('gold.beneficio_concedido') IS NULL
       OR to_regclass('gold.servico_prestado') IS NULL
       OR to_regclass('serving.registro_integrado') IS NULL THEN
        RAISE EXCEPTION 'Materialization core incompleto.';
    END IF;
END;
$$;

-- Catálogo factual de Serviço para provar fato válido sem identidade resolvida.
INSERT INTO ref.tipo_registro(codigo,nome,gestor_id,natureza,ativo)
SELECT 'SV01','Serviço smoke',g.gestor_id,'SERVICO',TRUE
FROM ref.gestor g WHERE g.codigo='SEHAB'
ON CONFLICT(codigo) DO UPDATE SET gestor_id=EXCLUDED.gestor_id,natureza='SERVICO',ativo=TRUE;

INSERT INTO ref.tipo_registro_versao(tipo_registro_id,versao,status,schema_registro_ref,schema_registro_sha256,qc_status,regime_vigencia)
SELECT tr.tipo_registro_id,1,'ATIVA','config/contracts/registros/SEHAB/SV01/v1/schema.json',decode(repeat('44',32),'hex'),'NAO_IMPLEMENTADO','NAO_APLICAVEL'
FROM ref.tipo_registro tr WHERE tr.codigo='SV01'
ON CONFLICT(tipo_registro_id,versao) DO NOTHING;

-- Entrega/lote exclusivos do smoke de materialização.
INSERT INTO ingestao.entrega(
    entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,natureza,tipo_registro_id,tipo_registro_versao_id,
    idempotency_key,payload_sha256,bytes_recebidos,status,data_referencia,recebido_em,ultima_atualizacao)
SELECT '71000000-0000-4000-8000-000000000001'::uuid,g.gestor_id,so.sistema_origem_id,gpv.gestor_pessoa_versao_id,
       'BENEFICIO',tr.tipo_registro_id,trv.tipo_registro_versao_id,'pg-materialization-smoke-benefit',repeat('b',64),100,
       'PROCESSADA','2026-07-17T00:00:00Z','2026-09-06T12:00:00Z','2026-09-06T12:01:00Z'
FROM ref.gestor g
JOIN ref.sistema_origem so ON so.gestor_id=g.gestor_id AND so.codigo='HabitaSampa'
JOIN ref.gestor_pessoa_versao gpv ON gpv.gestor_id=g.gestor_id AND gpv.versao=1
JOIN ref.tipo_registro tr ON tr.codigo='AA01'
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_id=tr.tipo_registro_id AND trv.versao=1
WHERE g.codigo='SEHAB'
ON CONFLICT(entrega_id) DO NOTHING;

INSERT INTO ingestao.entrega(
    entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,natureza,tipo_registro_id,tipo_registro_versao_id,
    idempotency_key,payload_sha256,bytes_recebidos,status,data_referencia,recebido_em,ultima_atualizacao)
SELECT '71000000-0000-4000-8000-000000000002'::uuid,g.gestor_id,so.sistema_origem_id,gpv.gestor_pessoa_versao_id,
       'SERVICO',tr.tipo_registro_id,trv.tipo_registro_versao_id,'pg-materialization-smoke-service',repeat('c',64),100,
       'PROCESSADA','2026-07-18T00:00:00Z','2026-09-06T12:00:00Z','2026-09-06T12:01:00Z'
FROM ref.gestor g
JOIN ref.sistema_origem so ON so.gestor_id=g.gestor_id AND so.codigo='HabitaSampa'
JOIN ref.gestor_pessoa_versao gpv ON gpv.gestor_id=g.gestor_id AND gpv.versao=1
JOIN ref.tipo_registro tr ON tr.codigo='SV01'
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_id=tr.tipo_registro_id AND trv.versao=1
WHERE g.codigo='SEHAB'
ON CONFLICT(entrega_id) DO NOTHING;

INSERT INTO ingestao.lote(lote_id,entrega_id,lote_seq,lote_total,status,criado_em,atualizado_em)
VALUES
('71000000-0000-4000-8000-000000000011','71000000-0000-4000-8000-000000000001',1,1,'PROCESSADO',CURRENT_TIMESTAMP,CURRENT_TIMESTAMP),
('71000000-0000-4000-8000-000000000012','71000000-0000-4000-8000-000000000002',1,1,'PROCESSADO',CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)
ON CONFLICT(lote_id) DO NOTHING;

INSERT INTO silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem)
SELECT so.sistema_origem_id,'CPF:16899535009'
FROM ref.sistema_origem so JOIN ref.gestor g ON g.gestor_id=so.gestor_id
WHERE g.codigo='SEHAB' AND so.codigo='HabitaSampa'
RETURNING pessoa_origem_id;

INSERT INTO silver.pessoa_observacao(
    pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,cpf,cpf_ausente_motivo,
    nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
SELECT po.pessoa_origem_id,'71000000-0000-4000-8000-000000000011',g.gestor_id,'CPF:16899535009',1,repeat('d',64),
       '16899535009',NULL,'MARIA DA SILVA','MARIA SILVA','1985-05-17','ANA DA SILVA','ANA SILVA','2026-07-17T00:00:00Z'
FROM silver.pessoa_origem po
JOIN ref.sistema_origem so ON so.sistema_origem_id=po.sistema_origem_id
JOIN ref.gestor g ON g.gestor_id=so.gestor_id
WHERE po.codigo_pessoa_origem='CPF:16899535009' AND g.codigo='SEHAB';

INSERT INTO identidade.pessoa(pessoa_uuid,status)
VALUES('72000000-0000-4000-8000-000000000001','ATIVO');

INSERT INTO identidade.identity_map(
    pessoa_uuid,tipo,identificador,vigencia_inicio,gestor_origem_id,source_record_id,metodo_resolucao,estado,estado_motivo)
SELECT '72000000-0000-4000-8000-000000000001','CPF','16899535009',CURRENT_TIMESTAMP,g.gestor_id,'CPF:16899535009',
       'CPF_DETERMINISTICO','ATIVO','SMOKE'
FROM ref.gestor g WHERE g.codigo='SEHAB';

INSERT INTO identidade.vinculo_fonte(
    pessoa_observacao_id,pessoa_uuid,metodo_resolucao,status,ativo,resolvido_em,motivo)
SELECT pob.pessoa_observacao_id,'72000000-0000-4000-8000-000000000001','CPF_DETERMINISTICO','RESOLVIDO',TRUE,CURRENT_TIMESTAMP,'SMOKE'
FROM silver.pessoa_observacao pob WHERE pob.codigo_pessoa_origem='CPF:16899535009';

INSERT INTO gold.pessoa(
    pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,atualizado_em)
VALUES('72000000-0000-4000-8000-000000000001','16899535009','PRESENTE','MARIA DA SILVA','1985-05-17','ANA DA SILVA',1,'BASELINE_FONTE_UNICA',CURRENT_TIMESTAMP);

INSERT INTO silver.registro_origem(sistema_origem_id,codigo_registro_origem,natureza,tipo_registro_id)
SELECT so.sistema_origem_id,'AA01:SMOKE','BENEFICIO',tr.tipo_registro_id
FROM ref.sistema_origem so
JOIN ref.gestor g ON g.gestor_id=so.gestor_id
JOIN ref.tipo_registro tr ON tr.codigo='AA01'
WHERE g.codigo='SEHAB' AND so.codigo='HabitaSampa';

INSERT INTO silver.registro_observacao(
    registro_origem_id,codigo_registro_origem,versao_interna,operacao,conteudo_hash,lote_id,gestor_id,natureza,
    tipo_registro_id,tipo_registro_versao_id,pessoa_observacao_id,data_inicio_concessao,situacao_vigencia,
    valor_concedido,source_as_of)
SELECT ro.registro_origem_id,'AA01:SMOKE',1,'INCLUSAO',repeat('e',64),'71000000-0000-4000-8000-000000000011',g.gestor_id,
       'BENEFICIO',tr.tipo_registro_id,trv.tipo_registro_versao_id,pob.pessoa_observacao_id,'2026-07-01','VIGENTE',1000.00,'2026-07-17T00:00:00Z'
FROM silver.registro_origem ro
JOIN ref.sistema_origem so ON so.sistema_origem_id=ro.sistema_origem_id
JOIN ref.gestor g ON g.gestor_id=so.gestor_id
JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ro.tipo_registro_id
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_id=tr.tipo_registro_id AND trv.versao=1
JOIN silver.pessoa_observacao pob ON pob.codigo_pessoa_origem='CPF:16899535009'
WHERE ro.codigo_registro_origem='AA01:SMOKE';

INSERT INTO gold.beneficio_concedido(
    registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,
    pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,cpf_ausente_motivo,pessoa_uuid,
    estado_atribuicao_identidade,gestor_id,tipo_registro_id,tipo_registro_versao_id,entrega_id,
    data_inicio_concessao,situacao_vigencia,valor_concedido,source_as_of,qc_especifico_implementado,
    vigencia_versao_inicio)
SELECT rob.registro_observacao_id,ro.registro_origem_id,ro.codigo_registro_origem,1,'INCLUSAO','VIGENTE',
       po.pessoa_origem_id,po.sistema_origem_id,po.codigo_pessoa_origem,'16899535009',NULL,
       '72000000-0000-4000-8000-000000000001','ATRIBUIDA',g.gestor_id,tr.tipo_registro_id,trv.tipo_registro_versao_id,
       '71000000-0000-4000-8000-000000000001',rob.data_inicio_concessao,'VIGENTE',rob.valor_concedido,
       rob.source_as_of,FALSE,CURRENT_TIMESTAMP
FROM silver.registro_observacao rob
JOIN silver.registro_origem ro ON ro.registro_origem_id=rob.registro_origem_id
JOIN silver.pessoa_observacao pob ON pob.pessoa_observacao_id=rob.pessoa_observacao_id
JOIN silver.pessoa_origem po ON po.pessoa_origem_id=pob.pessoa_origem_id
JOIN ref.gestor g ON g.gestor_id=rob.gestor_id
JOIN ref.tipo_registro tr ON tr.tipo_registro_id=rob.tipo_registro_id
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=rob.tipo_registro_versao_id
WHERE ro.codigo_registro_origem='AA01:SMOKE';

INSERT INTO serving.registro_integrado(
    registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,
    pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,cpf_ausente_motivo,pessoa_uuid,
    estado_atribuicao_identidade,gestor_id,natureza,tipo_registro_id,tipo_registro_versao_id,entrega_id,
    data_inicio_concessao,situacao_vigencia,valor_concedido,source_as_of,qc_especifico_implementado,
    vigencia_versao_inicio)
SELECT b.registro_observacao_id,b.registro_origem_id,b.codigo_registro_origem,b.versao_interna,b.operacao,b.status_analitico,
       b.pessoa_origem_id,b.sistema_origem_id,b.codigo_pessoa_origem,b.cpf_declarado,b.cpf_ausente_motivo,b.pessoa_uuid,
       b.estado_atribuicao_identidade,b.gestor_id,'BENEFICIO',b.tipo_registro_id,b.tipo_registro_versao_id,b.entrega_id,
       b.data_inicio_concessao,b.situacao_vigencia,b.valor_concedido,b.source_as_of,b.qc_especifico_implementado,
       b.vigencia_versao_inicio
FROM gold.beneficio_concedido b WHERE b.codigo_registro_origem='AA01:SMOKE';

-- Prova de versionamento: V1 deixa de ser corrente e V2 assume VIGENTE.
UPDATE gold.beneficio_concedido SET status_analitico='HISTORICO',vigencia_versao_fim=CURRENT_TIMESTAMP
WHERE codigo_registro_origem='AA01:SMOKE' AND status_analitico='VIGENTE';
UPDATE serving.registro_integrado SET status_analitico='HISTORICO',vigencia_versao_fim=CURRENT_TIMESTAMP
WHERE codigo_registro_origem='AA01:SMOKE' AND status_analitico='VIGENTE';

INSERT INTO silver.registro_observacao(
    registro_origem_id,codigo_registro_origem,versao_interna,operacao,conteudo_hash,lote_id,gestor_id,natureza,
    tipo_registro_id,tipo_registro_versao_id,pessoa_observacao_id,data_inicio_concessao,situacao_vigencia,
    valor_concedido,source_as_of)
SELECT ro.registro_origem_id,'AA01:SMOKE',2,'ALTERACAO',repeat('f',64),'71000000-0000-4000-8000-000000000011',g.gestor_id,
       'BENEFICIO',tr.tipo_registro_id,trv.tipo_registro_versao_id,pob.pessoa_observacao_id,'2026-07-01','VIGENTE',1200.00,'2026-07-17T00:00:00Z'
FROM silver.registro_origem ro
JOIN ref.sistema_origem so ON so.sistema_origem_id=ro.sistema_origem_id
JOIN ref.gestor g ON g.gestor_id=so.gestor_id
JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ro.tipo_registro_id
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_id=tr.tipo_registro_id AND trv.versao=1
JOIN silver.pessoa_observacao pob ON pob.codigo_pessoa_origem='CPF:16899535009'
WHERE ro.codigo_registro_origem='AA01:SMOKE';

INSERT INTO gold.beneficio_concedido(
    registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,
    pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,cpf_ausente_motivo,pessoa_uuid,
    estado_atribuicao_identidade,gestor_id,tipo_registro_id,tipo_registro_versao_id,entrega_id,
    data_inicio_concessao,situacao_vigencia,valor_concedido,source_as_of,qc_especifico_implementado,vigencia_versao_inicio)
SELECT rob.registro_observacao_id,ro.registro_origem_id,ro.codigo_registro_origem,2,'ALTERACAO','VIGENTE',
       po.pessoa_origem_id,po.sistema_origem_id,po.codigo_pessoa_origem,'16899535009',NULL,
       '72000000-0000-4000-8000-000000000001','ATRIBUIDA',g.gestor_id,tr.tipo_registro_id,trv.tipo_registro_versao_id,
       '71000000-0000-4000-8000-000000000001',rob.data_inicio_concessao,'VIGENTE',rob.valor_concedido,
       rob.source_as_of,FALSE,CURRENT_TIMESTAMP
FROM silver.registro_observacao rob
JOIN silver.registro_origem ro ON ro.registro_origem_id=rob.registro_origem_id
JOIN silver.pessoa_observacao pob ON pob.pessoa_observacao_id=rob.pessoa_observacao_id
JOIN silver.pessoa_origem po ON po.pessoa_origem_id=pob.pessoa_origem_id
JOIN ref.gestor g ON g.gestor_id=rob.gestor_id
JOIN ref.tipo_registro tr ON tr.tipo_registro_id=rob.tipo_registro_id
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=rob.tipo_registro_versao_id
WHERE ro.codigo_registro_origem='AA01:SMOKE' AND rob.versao_interna=2;

DO $$
DECLARE v_current INTEGER; v_history INTEGER;
BEGIN
    SELECT count(*) INTO v_current FROM gold.beneficio_concedido
     WHERE codigo_registro_origem='AA01:SMOKE' AND status_analitico='VIGENTE';
    SELECT count(*) INTO v_history FROM gold.beneficio_concedido
     WHERE codigo_registro_origem='AA01:SMOKE' AND status_analitico='HISTORICO';
    IF v_current<>1 OR v_history<>1 THEN
        RAISE EXCEPTION 'Versionamento factual PostgreSQL inválido: corrente=%, histórico=%',v_current,v_history;
    END IF;
END;
$$;

-- Fato de Serviço continua válido mesmo sem UUID municipal: ocorrência factual e identidade são independentes.
INSERT INTO silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem)
SELECT so.sistema_origem_id,'SEMCPF:SMOKE'
FROM ref.sistema_origem so JOIN ref.gestor g ON g.gestor_id=so.gestor_id
WHERE g.codigo='SEHAB' AND so.codigo='HabitaSampa';

INSERT INTO silver.pessoa_observacao(
    pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,cpf,cpf_ausente_motivo,
    nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
SELECT po.pessoa_origem_id,'71000000-0000-4000-8000-000000000012',g.gestor_id,'SEMCPF:SMOKE',1,repeat('1',64),NULL,
       'SEM_CPF','JOSE TESTE','JOSE TESTE','1990-01-01','MAE TESTE','MAE TESTE','2026-07-18T00:00:00Z'
FROM silver.pessoa_origem po
JOIN ref.sistema_origem so ON so.sistema_origem_id=po.sistema_origem_id
JOIN ref.gestor g ON g.gestor_id=so.gestor_id
WHERE po.codigo_pessoa_origem='SEMCPF:SMOKE';

INSERT INTO identidade.vinculo_fonte(pessoa_observacao_id,pessoa_uuid,metodo_resolucao,status,ativo,motivo)
SELECT pessoa_observacao_id,NULL,'PENDENTE_PROBABILISTICO','NAO_RESOLVIDO',TRUE,'SMOKE_PENDENTE'
FROM silver.pessoa_observacao WHERE codigo_pessoa_origem='SEMCPF:SMOKE';

INSERT INTO silver.registro_origem(sistema_origem_id,codigo_registro_origem,natureza,tipo_registro_id)
SELECT so.sistema_origem_id,'SV01:SMOKE','SERVICO',tr.tipo_registro_id
FROM ref.sistema_origem so
JOIN ref.gestor g ON g.gestor_id=so.gestor_id
JOIN ref.tipo_registro tr ON tr.codigo='SV01'
WHERE g.codigo='SEHAB' AND so.codigo='HabitaSampa';

INSERT INTO silver.registro_observacao(
    registro_origem_id,codigo_registro_origem,versao_interna,operacao,conteudo_hash,lote_id,gestor_id,natureza,
    tipo_registro_id,tipo_registro_versao_id,pessoa_observacao_id,data_hora_servico,unidade_servico,situacao,source_as_of)
SELECT ro.registro_origem_id,'SV01:SMOKE',1,'INCLUSAO',repeat('2',64),'71000000-0000-4000-8000-000000000012',g.gestor_id,
       'SERVICO',tr.tipo_registro_id,trv.tipo_registro_versao_id,pob.pessoa_observacao_id,'2026-07-18T10:00:00Z','UNIDADE TESTE','REALIZADO','2026-07-18T00:00:00Z'
FROM silver.registro_origem ro
JOIN ref.sistema_origem so ON so.sistema_origem_id=ro.sistema_origem_id
JOIN ref.gestor g ON g.gestor_id=so.gestor_id
JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ro.tipo_registro_id
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_id=tr.tipo_registro_id AND trv.versao=1
JOIN silver.pessoa_observacao pob ON pob.codigo_pessoa_origem='SEMCPF:SMOKE'
WHERE ro.codigo_registro_origem='SV01:SMOKE';

INSERT INTO gold.servico_prestado(
    registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,
    pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,cpf_ausente_motivo,pessoa_uuid,
    estado_atribuicao_identidade,gestor_id,tipo_registro_id,tipo_registro_versao_id,entrega_id,
    data_hora_servico,unidade_servico,situacao,source_as_of,vigencia_versao_inicio)
SELECT rob.registro_observacao_id,ro.registro_origem_id,ro.codigo_registro_origem,1,'INCLUSAO','VIGENTE',
       po.pessoa_origem_id,po.sistema_origem_id,po.codigo_pessoa_origem,NULL,'SEM_CPF',NULL,'PENDENTE_IDENTIDADE',
       g.gestor_id,tr.tipo_registro_id,trv.tipo_registro_versao_id,'71000000-0000-4000-8000-000000000002',
       rob.data_hora_servico,rob.unidade_servico,rob.situacao,rob.source_as_of,CURRENT_TIMESTAMP
FROM silver.registro_observacao rob
JOIN silver.registro_origem ro ON ro.registro_origem_id=rob.registro_origem_id
JOIN silver.pessoa_observacao pob ON pob.pessoa_observacao_id=rob.pessoa_observacao_id
JOIN silver.pessoa_origem po ON po.pessoa_origem_id=pob.pessoa_origem_id
JOIN ref.gestor g ON g.gestor_id=rob.gestor_id
JOIN ref.tipo_registro tr ON tr.tipo_registro_id=rob.tipo_registro_id
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=rob.tipo_registro_versao_id
WHERE ro.codigo_registro_origem='SV01:SMOKE';

DO $$
BEGIN
    IF NOT EXISTS(
        SELECT 1 FROM gold.servico_prestado
        WHERE codigo_registro_origem='SV01:SMOKE'
          AND pessoa_uuid IS NULL
          AND estado_atribuicao_identidade='PENDENTE_IDENTIDADE') THEN
        RAISE EXCEPTION 'Fato sem identidade não foi preservado.';
    END IF;

    BEGIN
        UPDATE gold.servico_prestado
           SET estado_atribuicao_identidade='ATRIBUIDA'
         WHERE codigo_registro_origem='SV01:SMOKE';
        RAISE EXCEPTION 'Constraint de atribuição deveria rejeitar ATRIBUIDA sem UUID.';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;
END;
$$;

-- Prova do índice parcial: duas versões VIGENTE do mesmo registro não são permitidas.
DO $$
BEGIN
    BEGIN
        UPDATE gold.beneficio_concedido
           SET status_analitico='VIGENTE',vigencia_versao_fim=NULL
         WHERE codigo_registro_origem='AA01:SMOKE' AND versao_interna=1;
        RAISE EXCEPTION 'Índice parcial deveria impedir duas versões VIGENTE.';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;
END;
$$;

ROLLBACK;

SELECT 'POSTGRESQL MATERIALIZATION CORE SMOKE: OK' AS resultado;
