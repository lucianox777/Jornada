-- Consulta de identidade progressiva por chave de origem, sem efeitos colaterais.
-- Não executa Linkage, não cria UUID e não altera vínculos ou fatos.
DO $$ BEGIN
 IF to_regclass('serving.v_identidade_origem_progressiva') IS NULL THEN
  RAISE EXCEPTION 'Projeção Serving da identidade progressiva não instalada.';
 END IF;
END $$;

CREATE OR REPLACE FUNCTION identidade.consultar_origem_progressiva(
 p_gestor VARCHAR(30), p_sistema VARCHAR(80), p_codigo VARCHAR(255))
RETURNS TABLE(
 pessoa_origem_id BIGINT, gestor_codigo VARCHAR, sistema_origem_codigo VARCHAR,
 codigo_pessoa_origem VARCHAR, initial_uuid UUID, canonical_uuid UUID,
 estado VARCHAR, versao BIGINT, apta_contagem_pessoa_canonica BOOLEAN,
 criado_em TIMESTAMPTZ, atualizado_em TIMESTAMPTZ, ultima_resolucao_em TIMESTAMPTZ)
LANGUAGE plpgsql STABLE SECURITY INVOKER
AS $$
BEGIN
 IF p_gestor IS NULL OR btrim(p_gestor)=''
    OR p_sistema IS NULL OR btrim(p_sistema)=''
    OR p_codigo IS NULL OR btrim(p_codigo)='' THEN
  RAISE EXCEPTION 'Chave de origem e Gestor são obrigatórios.' USING ERRCODE='22023';
 END IF;
 RETURN QUERY
 SELECT v.pessoa_origem_id,v.gestor_codigo::VARCHAR,v.sistema_origem_codigo::VARCHAR,
        v.codigo_pessoa_origem::VARCHAR,v.initial_uuid,v.canonical_uuid,v.estado::VARCHAR,
        v.versao,v.apta_contagem_pessoa_canonica,v.criado_em,v.atualizado_em,v.ultima_resolucao_em
 FROM serving.v_identidade_origem_progressiva v
 WHERE v.gestor_codigo=p_gestor
   AND v.sistema_origem_codigo=p_sistema
   AND v.codigo_pessoa_origem=p_codigo;
END $$;
