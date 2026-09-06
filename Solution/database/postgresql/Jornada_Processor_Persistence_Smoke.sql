-- Smoke de persistência do Processor PostgreSQL.
-- Deve ser executado após Resultado Core + Ingestion Processor Core + Processor Persistence Core.
-- O código técnico do sistema é SEHAB; HabitaSampa é apenas seu nome de exibição.

DO $$
DECLARE
    v_gestor BIGINT;
    v_sistema BIGINT;
    v_tipo_aa BIGINT;
    v_tipo_aa_v1 BIGINT;
    v_lote UUID := '70000000-0000-4000-8000-000000000002'::uuid;
    v_entrega UUID := '70000000-0000-4000-8000-000000000001'::uuid;
    v_pessoa_origem BIGINT;
    v_obs BIGINT;
    v_uuid UUID := 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaa91'::uuid;
    v_attr_endereco BIGINT;
    v_attr_ref BIGINT;
    v_sp_dom BIGINT;
    v_dist_dom BIGINT;
    v_sp_ref BIGINT;
    v_dist_ref BIGINT;
    v_ref_selecionada BIGINT;
    v_reg_origem BIGINT;
    v_reg_obs BIGINT;
BEGIN
    SELECT gestor_id INTO STRICT v_gestor FROM ref.gestor WHERE codigo='SEHAB';
    SELECT sistema_origem_id INTO STRICT v_sistema
      FROM ref.sistema_origem WHERE gestor_id=v_gestor AND codigo='SEHAB';
    IF NOT EXISTS (
        SELECT 1 FROM ingestao.entrega
        WHERE entrega_id=v_entrega AND gestor_id=v_gestor AND sistema_origem_id=v_sistema
    ) THEN
        RAISE EXCEPTION 'Fixture de Entrega SEHAB não corresponde ao sistema de origem canônico.';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM ingestao.lote WHERE lote_id=v_lote AND entrega_id=v_entrega
    ) THEN
        RAISE EXCEPTION 'Fixture de Lote não pertence à Entrega SEHAB esperada.';
    END IF;
    SELECT tipo_registro_id INTO STRICT v_tipo_aa FROM ref.tipo_registro WHERE codigo='AA01';
    SELECT tipo_registro_versao_id INTO STRICT v_tipo_aa_v1
      FROM ref.tipo_registro_versao WHERE tipo_registro_id=v_tipo_aa AND versao=1;

    IF (SELECT COUNT(*) FROM ref.atributo_transversal WHERE ativo) < 6 THEN
        RAISE EXCEPTION 'Catálogo transversal PostgreSQL incompleto.';
    END IF;

    INSERT INTO silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem)
    VALUES(v_sistema,'PG-PERSIST-001')
    ON CONFLICT(sistema_origem_id,codigo_pessoa_origem) DO UPDATE
       SET ultima_recepcao_em=CURRENT_TIMESTAMP
    RETURNING pessoa_origem_id INTO v_pessoa_origem;

    INSERT INTO silver.pessoa_observacao(
        pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
        cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
    VALUES(
        v_pessoa_origem,v_lote,v_gestor,'PG-PERSIST-001',1,repeat('b',64),
        '11144477735',NULL,'Maria da Silva','MARIA SILVA','1982-04-10','Ana de Souza','ANA SOUZA','2026-07-17T00:00:00Z')
    ON CONFLICT(pessoa_origem_id,versao_interna) DO UPDATE SET source_as_of=EXCLUDED.source_as_of
    RETURNING pessoa_observacao_id INTO v_obs;

    INSERT INTO identidade.pessoa(pessoa_uuid,status)
    VALUES(v_uuid,'ATIVO')
    ON CONFLICT(pessoa_uuid) DO UPDATE SET status='ATIVO',atualizado_em=CURRENT_TIMESTAMP;

    INSERT INTO identidade.identity_map(
        pessoa_uuid,tipo,identificador,gestor_origem_id,source_record_id,metodo_resolucao,estado,estado_motivo)
    VALUES(v_uuid,'CPF','11144477735',v_gestor,'PG-PERSIST-001','CPF_DETERMINISTICO','ATIVO','SMOKE')
    ON CONFLICT(tipo,identificador) WHERE vigencia_fim IS NULL DO NOTHING;

    INSERT INTO identidade.vinculo_fonte(
        pessoa_observacao_id,pessoa_uuid,metodo_resolucao,status,ativo,resolvido_em,motivo)
    VALUES(v_obs,v_uuid,'CPF_DETERMINISTICO','RESOLVIDO',TRUE,CURRENT_TIMESTAMP,'SMOKE')
    ON CONFLICT(pessoa_observacao_id) WHERE ativo DO UPDATE
       SET pessoa_uuid=EXCLUDED.pessoa_uuid,status='RESOLVIDO',metodo_resolucao='CPF_DETERMINISTICO',resolvido_em=CURRENT_TIMESTAMP;

    INSERT INTO gold.pessoa(
        pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia)
    VALUES(v_uuid,'11144477735','PRESENTE','Maria da Silva','1982-04-10','Ana de Souza',1,'BASELINE_FONTE_UNICA')
    ON CONFLICT(pessoa_uuid) DO UPDATE SET
        cpf=EXCLUDED.cpf,nome_completo=EXCLUDED.nome_completo,data_nascimento=EXCLUDED.data_nascimento,
        nome_mae=EXCLUDED.nome_mae,atualizado_em=CURRENT_TIMESTAMP;

    INSERT INTO ref.subprefeitura(codigo,nome)
    VALUES('SP-DOM','Subprefeitura Domiciliar')
    ON CONFLICT(codigo,nome) DO UPDATE SET observado_em=CURRENT_TIMESTAMP
    RETURNING subprefeitura_id INTO v_sp_dom;
    INSERT INTO ref.distrito(subprefeitura_id,codigo,nome)
    VALUES(v_sp_dom,'D-DOM','Distrito Domiciliar')
    ON CONFLICT(subprefeitura_id,codigo,nome) DO UPDATE SET observado_em=CURRENT_TIMESTAMP
    RETURNING distrito_id INTO v_dist_dom;

    INSERT INTO ref.subprefeitura(codigo,nome)
    VALUES('SP-REF','Subprefeitura Referência')
    ON CONFLICT(codigo,nome) DO UPDATE SET observado_em=CURRENT_TIMESTAMP
    RETURNING subprefeitura_id INTO v_sp_ref;
    INSERT INTO ref.distrito(subprefeitura_id,codigo,nome)
    VALUES(v_sp_ref,'D-REF','Distrito Referência')
    ON CONFLICT(subprefeitura_id,codigo,nome) DO UPDATE SET observado_em=CURRENT_TIMESTAMP
    RETURNING distrito_id INTO v_dist_ref;

    INSERT INTO silver.pessoa_atributo_observacao(
        source_record_id,pessoa_observacao_id,fonte_gestor_id,atributo_codigo,atributo_instancia_chave,
        valor,status_evidencia,evidencia_tipo,referencia_evidencia,verificado_em)
    VALUES('END-1',v_obs,v_gestor,'ENDERECO_RESIDENCIAL','UNICA','Rua Domiciliar, 1','COMPROVADO','DOCUMENTO',
           '2026-07-17T00:00:00Z','2026-07-17T00:00:00Z')
    ON CONFLICT(pessoa_observacao_id,atributo_codigo,atributo_instancia_chave) DO UPDATE SET valor=EXCLUDED.valor
    RETURNING pessoa_atributo_observacao_id INTO v_attr_endereco;

    INSERT INTO silver.referencia_territorial_observacao(
        pessoa_atributo_observacao_id,natureza_referencia,fonte_semantica,subprefeitura_id,distrito_id,
        situacao_geografia,origem_geografia,referencia_malha,resolvido_em)
    VALUES(v_attr_endereco,'DOMICILIAR','ENDERECO_RESIDENCIAL',v_sp_dom,v_dist_dom,
           'RESOLVIDA','ORIGEM','SMOKE-2026','2026-07-17T00:00:00Z')
    ON CONFLICT(pessoa_atributo_observacao_id) DO UPDATE SET subprefeitura_id=EXCLUDED.subprefeitura_id,distrito_id=EXCLUDED.distrito_id
    RETURNING referencia_territorial_observacao_id INTO v_ref_selecionada;

    INSERT INTO silver.pessoa_atributo_observacao(
        source_record_id,pessoa_observacao_id,fonte_gestor_id,atributo_codigo,atributo_instancia_chave,
        valor,status_evidencia,evidencia_tipo,referencia_evidencia,verificado_em)
    VALUES('REF-1',v_obs,v_gestor,'REFERENCIA_TERRITORIAL','UNICA','Território declarado','DECLARADO','SISTEMA_ORIGEM',
           '2026-07-16T00:00:00Z',NULL)
    ON CONFLICT(pessoa_observacao_id,atributo_codigo,atributo_instancia_chave) DO UPDATE SET valor=EXCLUDED.valor
    RETURNING pessoa_atributo_observacao_id INTO v_attr_ref;

    INSERT INTO silver.referencia_territorial_observacao(
        pessoa_atributo_observacao_id,natureza_referencia,fonte_semantica,subprefeitura_id,distrito_id,
        situacao_geografia,origem_geografia,referencia_malha,resolvido_em)
    VALUES(v_attr_ref,'REFERENCIA_TERRITORIAL_DECLARADA','REFERENCIA_TERRITORIAL',v_sp_ref,v_dist_ref,
           'RESOLVIDA','ORIGEM','SMOKE-2026','2026-07-16T00:00:00Z')
    ON CONFLICT(pessoa_atributo_observacao_id) DO UPDATE SET subprefeitura_id=EXCLUDED.subprefeitura_id,distrito_id=EXCLUDED.distrito_id;

    SELECT referencia_territorial_observacao_id INTO STRICT v_ref_selecionada
      FROM silver.v_pessoa_referencia_territorial WHERE pessoa_observacao_id=v_obs;
    IF v_ref_selecionada <> (SELECT referencia_territorial_observacao_id FROM silver.referencia_territorial_observacao WHERE pessoa_atributo_observacao_id=v_attr_ref) THEN
        RAISE EXCEPTION 'Referência Territorial explícita não teve precedência sobre endereço residencial.';
    END IF;

    INSERT INTO silver.registro_origem(sistema_origem_id,natureza,tipo_registro_id,codigo_registro_origem)
    VALUES(v_sistema,'BENEFICIO',v_tipo_aa,'PG-AA-001')
    ON CONFLICT(sistema_origem_id,natureza,tipo_registro_id,codigo_registro_origem) DO UPDATE
       SET ultima_recepcao_em=CURRENT_TIMESTAMP
    RETURNING registro_origem_id INTO v_reg_origem;

    INSERT INTO silver.registro_observacao(
        registro_origem_id,codigo_registro_origem,versao_interna,operacao,conteudo_hash,lote_id,gestor_id,
        natureza,tipo_registro_id,tipo_registro_versao_id,pessoa_observacao_id,
        data_inicio_concessao,situacao_vigencia,valor_concedido,source_as_of)
    VALUES(v_reg_origem,'PG-AA-001',1,'INCLUSAO',repeat('c',64),v_lote,v_gestor,
           'BENEFICIO',v_tipo_aa,v_tipo_aa_v1,v_obs,'2026-07-01','ATIVA',500.00,'2026-07-17T00:00:00Z')
    ON CONFLICT(registro_origem_id,versao_interna) DO UPDATE SET source_as_of=EXCLUDED.source_as_of
    RETURNING registro_observacao_id INTO v_reg_obs;

    -- Deliberadamente sem UUID: prova que ocorrência factual válida não depende da atribuição canônica.
    INSERT INTO gold.beneficio_concedido(
        registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,
        pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,pessoa_uuid,estado_atribuicao_identidade,
        gestor_id,tipo_registro_id,tipo_registro_versao_id,entrega_id,data_inicio_concessao,situacao_vigencia,
        valor_concedido,source_as_of,qc_especifico_implementado,vigencia_versao_inicio)
    VALUES(v_reg_obs,v_reg_origem,'PG-AA-001',1,'INCLUSAO','VIGENTE',v_pessoa_origem,v_sistema,'PG-PERSIST-001',
           '11144477735',NULL,'PENDENTE_IDENTIDADE',v_gestor,v_tipo_aa,v_tipo_aa_v1,v_entrega,'2026-07-01','ATIVA',
           500.00,'2026-07-17T00:00:00Z',FALSE,CURRENT_TIMESTAMP)
    ON CONFLICT(registro_observacao_id) DO UPDATE SET atualizado_em=CURRENT_TIMESTAMP;

    INSERT INTO serving.registro_integrado(
        registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,
        pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,pessoa_uuid,estado_atribuicao_identidade,
        gestor_id,natureza,tipo_registro_id,tipo_registro_versao_id,entrega_id,entrega_completa,
        data_inicio_concessao,situacao_vigencia,valor_concedido,source_as_of,qc_especifico_implementado,vigencia_versao_inicio)
    VALUES(v_reg_obs,v_reg_origem,'PG-AA-001',1,'INCLUSAO','VIGENTE',v_pessoa_origem,v_sistema,'PG-PERSIST-001',
           '11144477735',NULL,'PENDENTE_IDENTIDADE',v_gestor,'BENEFICIO',v_tipo_aa,v_tipo_aa_v1,v_entrega,FALSE,
           '2026-07-01','ATIVA',500.00,'2026-07-17T00:00:00Z',FALSE,CURRENT_TIMESTAMP)
    ON CONFLICT(registro_observacao_id) DO UPDATE SET atualizado_em=CURRENT_TIMESTAMP;

    IF NOT EXISTS(
        SELECT 1 FROM gold.beneficio_concedido
         WHERE registro_observacao_id=v_reg_obs
           AND pessoa_uuid IS NULL
           AND estado_atribuicao_identidade='PENDENTE_IDENTIDADE'
           AND status_analitico='VIGENTE') THEN
        RAISE EXCEPTION 'Fato sem UUID não permaneceu materializado em Gold.';
    END IF;

    IF NOT EXISTS(
        SELECT 1 FROM serving.registro_integrado
         WHERE registro_observacao_id=v_reg_obs AND natureza='BENEFICIO' AND status_analitico='VIGENTE') THEN
        RAISE EXCEPTION 'Fato Gold não foi representado no Serving do smoke.';
    END IF;
END;
$$;

-- A constraint deve impedir estados de identidade incoerentes.
DO $$
BEGIN
    BEGIN
        INSERT INTO gold.beneficio_concedido(
            registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,
            pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,pessoa_uuid,estado_atribuicao_identidade,
            gestor_id,tipo_registro_id,tipo_registro_versao_id,entrega_id,source_as_of,vigencia_versao_inicio)
        SELECT ro.registro_observacao_id,ro.registro_origem_id,'INVALID-SMOKE',99,'INCLUSAO','VIGENTE',
               po.pessoa_origem_id,so.sistema_origem_id,'INVALID-SMOKE',NULL,'ATRIBUIDA',
               g.gestor_id,tr.tipo_registro_id,trv.tipo_registro_versao_id,e.entrega_id,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP
          FROM silver.registro_observacao ro
          JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ro.pessoa_observacao_id
          JOIN ref.gestor g ON g.gestor_id=ro.gestor_id
          JOIN ref.sistema_origem so ON so.sistema_origem_id=(SELECT sistema_origem_id FROM silver.pessoa_origem WHERE pessoa_origem_id=po.pessoa_origem_id)
          JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ro.tipo_registro_id
          JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=ro.tipo_registro_versao_id
          JOIN ingestao.entrega e ON e.entrega_id='70000000-0000-4000-8000-000000000001'::uuid
         LIMIT 1;
        RAISE EXCEPTION 'Constraint de estado de identidade não bloqueou ATRIBUIDA sem UUID.';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;
END;
$$;

SELECT 'POSTGRESQL PROCESSOR PERSISTENCE CORE: OK' AS resultado;
