INSERT INTO ref.gestor(codigo,nome)
VALUES('SEHAB','Secretaria Municipal de Habitação')
ON CONFLICT(codigo) DO NOTHING;

-- Código técnico canônico separado do nome de exibição.
INSERT INTO ref.sistema_origem(gestor_id,codigo,nome)
SELECT gestor_id,'SEHAB','HabitaSampa'
FROM ref.gestor WHERE codigo='SEHAB'
ON CONFLICT(gestor_id,codigo) DO UPDATE SET nome=EXCLUDED.nome;

INSERT INTO ref.gestor_pessoa_versao(gestor_id,versao)
SELECT gestor_id,1 FROM ref.gestor WHERE codigo='SEHAB'
ON CONFLICT(gestor_id,versao) DO NOTHING;

INSERT INTO ref.tipo_registro(codigo,nome)
VALUES('AA01','Auxílio Aluguel')
ON CONFLICT(codigo) DO NOTHING;

INSERT INTO ref.tipo_registro(codigo,nome)
VALUES('AE01','Auxílio Emergencial')
ON CONFLICT(codigo) DO NOTHING;

-- Prova de compatibilidade com o contrato canônico de quatro caracteres alfanuméricos.
INSERT INTO ref.tipo_registro(codigo,nome)
VALUES('CRA1','Smoke tipo alfanumérico')
ON CONFLICT(codigo) DO NOTHING;

INSERT INTO ref.tipo_registro_versao(tipo_registro_id,versao)
SELECT tipo_registro_id,1 FROM ref.tipo_registro WHERE codigo IN('AA01','AE01')
ON CONFLICT(tipo_registro_id,versao) DO NOTHING;

UPDATE ref.gestor SET ativo=TRUE WHERE codigo='SEHAB';
UPDATE ref.sistema_origem SET ativo=TRUE,nome='HabitaSampa'
 WHERE gestor_id=(SELECT gestor_id FROM ref.gestor WHERE codigo='SEHAB') AND codigo='SEHAB';
UPDATE ref.gestor_pessoa_versao
   SET status='ATIVA',
       pessoa_schema_ref='config/contracts/pessoas/SEHAB/v1/schema.json',
       pessoa_schema_sha256=decode(repeat('11',32),'hex')
 WHERE gestor_id=(SELECT gestor_id FROM ref.gestor WHERE codigo='SEHAB') AND versao=1;
UPDATE ref.tipo_registro
   SET gestor_id=(SELECT gestor_id FROM ref.gestor WHERE codigo='SEHAB'),
       natureza='BENEFICIO',ativo=TRUE
 WHERE codigo IN('AA01','AE01');
UPDATE ref.tipo_registro_versao trv
   SET status='ATIVA',
       schema_registro_ref=CASE tr.codigo
           WHEN 'AA01' THEN 'config/contracts/registros/SEHAB/AA01/v1/schema.json'
           ELSE 'config/contracts/registros/SEHAB/AE01/v1/schema.json' END,
       schema_registro_sha256=decode(CASE tr.codigo WHEN 'AA01' THEN repeat('22',32) ELSE repeat('33',32) END,'hex'),
       qc_status='ATIVO',regime_vigencia='VIGENCIA_DECLARADA'
  FROM ref.tipo_registro tr
 WHERE tr.tipo_registro_id=trv.tipo_registro_id AND tr.codigo IN('AA01','AE01') AND trv.versao=1;

INSERT INTO ingestao.entrega(
    entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,natureza,
    tipo_registro_id,tipo_registro_versao_id,idempotency_key,payload_sha256,
    bytes_recebidos,status,data_referencia,recebido_em,ultima_atualizacao)
SELECT
    '70000000-0000-4000-8000-000000000001'::uuid,
    g.gestor_id,so.sistema_origem_id,gpv.gestor_pessoa_versao_id,'BENEFICIO',
    tr.tipo_registro_id,trv.tipo_registro_versao_id,'pg-smoke-001',
    repeat('a',64),12345,'PROCESSADA','2026-07-17T00:00:00Z'::timestamptz,
    '2026-09-06T12:00:00Z'::timestamptz,'2026-09-06T12:01:00Z'::timestamptz
FROM ref.gestor g
JOIN ref.sistema_origem so ON so.gestor_id=g.gestor_id AND so.codigo='SEHAB'
JOIN ref.gestor_pessoa_versao gpv ON gpv.gestor_id=g.gestor_id AND gpv.versao=1
JOIN ref.tipo_registro tr ON tr.codigo='AA01'
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_id=tr.tipo_registro_id AND trv.versao=1
WHERE g.codigo='SEHAB'
ON CONFLICT(entrega_id) DO NOTHING;

INSERT INTO bronze.entrega_arquivo(
    entrega_id,nome_arquivo,content_type,objeto_chave,payload_sha256,tamanho_bytes,recebido_em)
VALUES(
    '70000000-0000-4000-8000-000000000001'::uuid,
    'ENTREGA_SEHAB_SEHAB_v2_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.zip',
    'application/zip','sha256/aa/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
    repeat('a',64),12345,'2026-09-06T12:00:00Z'::timestamptz)
ON CONFLICT(entrega_id) DO NOTHING;

INSERT INTO ingestao.lote(
    lote_id,entrega_id,lote_seq,lote_total,qtd_pessoas,qtd_registros,status,
    tentativa_count,recuperacao_count,criado_em,atualizado_em)
VALUES(
    '70000000-0000-4000-8000-000000000002'::uuid,
    '70000000-0000-4000-8000-000000000001'::uuid,
    1,1,1,1,'PROCESSADO',1,0,
    '2026-09-06T12:00:10Z'::timestamptz,'2026-09-06T12:01:00Z'::timestamptz)
ON CONFLICT(lote_id) DO NOTHING;

INSERT INTO ingestao.item_processado(lote_id,classe_item,resultado)
SELECT '70000000-0000-4000-8000-000000000002'::uuid,'PESSOA','PROCESSADO'
WHERE NOT EXISTS(
    SELECT 1 FROM ingestao.item_processado
    WHERE lote_id='70000000-0000-4000-8000-000000000002'::uuid
      AND classe_item='PESSOA' AND resultado='PROCESSADO');

INSERT INTO ingestao.item_processado(lote_id,classe_item,resultado)
SELECT '70000000-0000-4000-8000-000000000002'::uuid,'REGISTRO','PROCESSADO'
WHERE NOT EXISTS(
    SELECT 1 FROM ingestao.item_processado
    WHERE lote_id='70000000-0000-4000-8000-000000000002'::uuid
      AND classe_item='REGISTRO' AND resultado='PROCESSADO');

INSERT INTO ingestao.item_processado_resumo(entrega_id,classe_item,resultado,quantidade)
VALUES('70000000-0000-4000-8000-000000000001'::uuid,'REGISTRO','RETRANSMITIDO',3)
ON CONFLICT(entrega_id,classe_item,resultado)
DO UPDATE SET quantidade=EXCLUDED.quantidade,atualizado_em=CURRENT_TIMESTAMP;
