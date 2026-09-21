SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
 NIS/PIS/PASEP/NIT e RG como identificadores secundários governados
 ------------------------------------------------------------------
 - pessoa_uuid é a identidade canônica interna;
 - CPF permanece a única âncora externa determinística principal;
 - NIS/PIS/PASEP/NIT e RG são identificadores secundários: podem corroborar,
   permitir controle de qualidade e reencontro governado, mas não constituem
   Pessoa, não criam identity_map e não resolvem vínculo automaticamente;
 - múltiplos NIS podem coexistir historicamente para a mesma Pessoa;
 - o mesmo NIS observado sob Pessoas distintas é sinal de qualidade, não regra
   de fusão automática.
*/

IF OBJECT_ID(N'ref.tipo_identificador_pessoa',N'U') IS NULL
   OR OBJECT_ID(N'silver.pessoa_identificador_observacao',N'U') IS NULL
   OR OBJECT_ID(N'identidade.v_vinculo_corrente',N'V') IS NULL
   OR OBJECT_ID(N'serving.v_bi_linkage',N'V') IS NULL
    THROW 51960,'Pré-requisitos de identificadores secundários não instalados.',1;
GO

MERGE ref.tipo_identificador_pessoa AS t
USING (VALUES
    (N'NIS',N'Inscrição social CNIS (NIS/PIS/PASEP/NIT)',N'NIS_PIS_PASEP_NIT',N'NIS_BR_11_V1',N'NAO_AUTOMATICA',N'NAO_HIERARQUICO',CAST(NULL AS SMALLINT))
) AS s(tipo_identificador_codigo,nome,namespace_padrao,formato_codigo,elegibilidade_deterministica,papel_resolucao,prioridade_resolucao)
ON t.tipo_identificador_codigo=s.tipo_identificador_codigo
WHEN NOT MATCHED THEN
    INSERT(tipo_identificador_codigo,nome,exige_namespace,formato_codigo,elegibilidade_deterministica,papel_resolucao,prioridade_resolucao)
    VALUES(s.tipo_identificador_codigo,s.nome,1,s.formato_codigo,s.elegibilidade_deterministica,s.papel_resolucao,s.prioridade_resolucao)
WHEN MATCHED THEN UPDATE SET
    nome=s.nome,
    exige_namespace=1,
    formato_codigo=s.formato_codigo,
    elegibilidade_deterministica=s.elegibilidade_deterministica,
    papel_resolucao=s.papel_resolucao,
    prioridade_resolucao=s.prioridade_resolucao,
    ativo=1;
GO

/* RG já era persistido como identificador tipado e nunca foi writer determinístico.
   A classificação do catálogo é alinhada ao papel efetivo: secundário, não-âncora. */
UPDATE ref.tipo_identificador_pessoa
SET elegibilidade_deterministica=N'NAO_AUTOMATICA',
    papel_resolucao=N'NAO_HIERARQUICO',
    prioridade_resolucao=NULL,
    ativo=1
WHERE tipo_identificador_codigo=N'RG';
GO

IF OBJECT_ID(N'silver.CK_pessoa_identificador_nis_namespace',N'C') IS NOT NULL
    ALTER TABLE silver.pessoa_identificador_observacao
      DROP CONSTRAINT CK_pessoa_identificador_nis_namespace;
GO
ALTER TABLE silver.pessoa_identificador_observacao WITH CHECK
ADD CONSTRAINT CK_pessoa_identificador_nis_namespace CHECK(
    tipo_identificador_codigo<>N'NIS'
    OR namespace_codigo IN(N'NIS',N'PIS',N'PASEP',N'NIT'));
GO

/* A view nunca publica o valor do NIS/RG. O possível conflito de NIS é calculado
   a partir das atribuições correntes já resolvidas por mecanismos próprios. */
CREATE OR ALTER VIEW serving.v_bi_identificador_secundario_qualidade AS
WITH nis_por_valor AS(
    SELECT
      i.valor_normalizado,
      COUNT(DISTINCT vf.pessoa_uuid) qtd_pessoas_correntes
    FROM silver.pessoa_identificador_observacao i
    JOIN identidade.v_vinculo_corrente vf
      ON vf.pessoa_observacao_id=i.pessoa_observacao_id
     AND vf.status=N'RESOLVIDO'
     AND vf.pessoa_uuid IS NOT NULL
    WHERE i.tipo_identificador_codigo=N'NIS'
    GROUP BY i.valor_normalizado
), por_observacao AS(
    SELECT
      i.pessoa_observacao_id,
      SUM(CASE WHEN i.tipo_identificador_codigo=N'NIS' THEN 1 ELSE 0 END) qtd_nis,
      SUM(CASE WHEN i.tipo_identificador_codigo=N'NIS' AND i.status_validacao=N'VALIDO' THEN 1 ELSE 0 END) qtd_nis_validos_estruturais,
      SUM(CASE WHEN i.tipo_identificador_codigo=N'NIS' AND i.status_validacao=N'INVALIDO' THEN 1 ELSE 0 END) qtd_nis_invalidos,
      SUM(CASE WHEN i.tipo_identificador_codigo=N'NIS' AND i.status_evidencia=N'COMPROVADO' THEN 1 ELSE 0 END) qtd_nis_comprovados,
      MAX(CASE WHEN i.tipo_identificador_codigo=N'NIS' AND COALESCE(n.qtd_pessoas_correntes,0)>1 THEN 1 ELSE 0 END) nis_multiplas_pessoas,
      SUM(CASE WHEN i.tipo_identificador_codigo=N'RG' THEN 1 ELSE 0 END) qtd_rg,
      SUM(CASE WHEN i.tipo_identificador_codigo=N'RG' AND i.status_evidencia=N'COMPROVADO' THEN 1 ELSE 0 END) qtd_rg_comprovados
    FROM silver.pessoa_identificador_observacao i
    LEFT JOIN nis_por_valor n
      ON n.valor_normalizado=i.valor_normalizado
     AND i.tipo_identificador_codigo=N'NIS'
    WHERE i.tipo_identificador_codigo IN(N'NIS',N'RG')
    GROUP BY i.pessoa_observacao_id
)
SELECT
  po.pessoa_observacao_id,
  CAST(COALESCE(q.qtd_nis,0) AS INT) qtd_nis,
  CAST(COALESCE(q.qtd_nis_validos_estruturais,0) AS INT) qtd_nis_validos_estruturais,
  CAST(COALESCE(q.qtd_nis_invalidos,0) AS INT) qtd_nis_invalidos,
  CAST(COALESCE(q.qtd_nis_comprovados,0) AS INT) qtd_nis_comprovados,
  CAST(COALESCE(q.nis_multiplas_pessoas,0) AS INT) nis_multiplas_pessoas,
  CASE
    WHEN COALESCE(q.qtd_nis,0)=0 THEN N'SEM_NIS'
    WHEN COALESCE(q.nis_multiplas_pessoas,0)=1 THEN N'NIS_ASSOCIADO_MULTIPLAS_PESSOAS'
    WHEN COALESCE(q.qtd_nis_invalidos,0)>0 THEN N'NIS_ESTRUTURALMENTE_INVALIDO'
    WHEN COALESCE(q.qtd_nis_comprovados,0)>0 THEN N'NIS_COMPROVADO'
    WHEN COALESCE(q.qtd_nis_validos_estruturais,0)>0 THEN N'NIS_VALIDO_DECLARADO'
    ELSE N'NIS_DECLARADO'
  END nis_classificacao,
  CAST(CASE
    WHEN COALESCE(q.nis_multiplas_pessoas,0)=1 OR COALESCE(q.qtd_nis_invalidos,0)>0
    THEN 1 ELSE 0 END AS INT) nis_problema,
  CAST(COALESCE(q.qtd_rg,0) AS INT) qtd_rg,
  CAST(COALESCE(q.qtd_rg_comprovados,0) AS INT) qtd_rg_comprovados,
  CASE
    WHEN COALESCE(q.qtd_rg,0)=0 THEN N'SEM_RG'
    WHEN COALESCE(q.qtd_rg_comprovados,0)>0 THEN N'RG_COMPROVADO'
    ELSE N'RG_DECLARADO'
  END rg_classificacao
FROM silver.pessoa_observacao po
LEFT JOIN por_observacao q
  ON q.pessoa_observacao_id=po.pessoa_observacao_id;
GO

CREATE OR ALTER VIEW serving.v_bi_qualidade_identidade_origem AS
SELECT
  l.*,
  s.qtd_nis,
  s.qtd_nis_validos_estruturais,
  s.qtd_nis_invalidos,
  s.qtd_nis_comprovados,
  s.nis_multiplas_pessoas,
  s.nis_classificacao,
  s.nis_problema,
  s.qtd_rg,
  s.qtd_rg_comprovados,
  s.rg_classificacao
FROM serving.v_bi_linkage l
LEFT JOIN serving.v_bi_identificador_secundario_qualidade s
  ON s.pessoa_observacao_id=l.pessoa_observacao_id;
GO
