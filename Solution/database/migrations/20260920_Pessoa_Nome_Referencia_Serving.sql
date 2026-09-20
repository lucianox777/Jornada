SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Nome de referência no serving
  -----------------------------
  Referência de apresentação e evidência de identidade são conceitos independentes.

  - gold.pessoa.nome_completo continua sendo dado interno do núcleo Golden e não é
    substituído por nome social.
  - NOME_SOCIAL corrente, quando existir, tem precedência de apresentação.
  - a seleção é derivada, rastreável e reversível: a origem da referência fica explícita.
  - nenhuma coluna nome_referencia é criada em gold/identidade e o linkage não passa
    a consumir esta view.
*/

IF OBJECT_ID(N'gold.pessoa',N'U') IS NULL
   OR OBJECT_ID(N'gold.v_pessoa_atributo_corrente',N'V') IS NULL
    THROW 51840,'Nome de referência exige gold.pessoa e gold.v_pessoa_atributo_corrente instalados.',1;
GO

CREATE OR ALTER VIEW serving.v_pessoa_nome_referencia AS
SELECT gp.pessoa_uuid,
       COALESCE(ns.valor,gp.nome_completo) AS nome_referencia,
       CASE WHEN ns.pessoa_atributo_id IS NULL THEN N'NOME_CIVIL' ELSE N'NOME_SOCIAL' END AS nome_referencia_tipo,
       CASE WHEN ns.pessoa_atributo_id IS NULL THEN N'GOLD_PESSOA' ELSE N'GOLD_PESSOA_ATRIBUTO' END AS nome_referencia_fonte_tipo,
       ns.pessoa_atributo_observacao_id AS nome_referencia_fonte_observacao_id,
       ns.fonte_gestor_id AS nome_referencia_fonte_gestor_id,
       ns.source_record_id AS nome_referencia_source_record_id,
       CASE
         WHEN ns.pessoa_atributo_id IS NULL THEN gp.atualizado_em
         ELSE COALESCE(ns.precedencia_em,ns.verificado_em,ns.atualizado_em)
       END AS nome_referencia_em
FROM gold.pessoa gp
OUTER APPLY(
    SELECT TOP(1)
           a.pessoa_atributo_id,a.valor,a.fonte_gestor_id,
           a.pessoa_atributo_observacao_id,a.source_record_id,
           a.verificado_em,a.precedencia_em,a.atualizado_em
    FROM gold.v_pessoa_atributo_corrente a
    WHERE a.pessoa_uuid=gp.pessoa_uuid
      AND a.atributo_codigo=N'NOME_SOCIAL'
      AND NULLIF(LTRIM(RTRIM(a.valor)),N'') IS NOT NULL
    ORDER BY a.precedencia_em DESC,a.verificado_em DESC,a.pessoa_atributo_id DESC
) ns;
GO

/*
  Superfície padrão de Pessoa: o nome civil deixa de ser coluna de serving.
  Consumidores que precisam do dado civil para atividade administrativa interna
  devem usar uma superfície interna explicitamente autorizada, não esta view.
*/
CREATE OR ALTER VIEW serving.v_pessoa AS
SELECT gp.pessoa_uuid,gp.cpf,gp.status_cpf,
       nr.nome_referencia,nr.nome_referencia_tipo,
       nr.nome_referencia_fonte_tipo,nr.nome_referencia_fonte_observacao_id,
       nr.nome_referencia_fonte_gestor_id,nr.nome_referencia_source_record_id,
       nr.nome_referencia_em,
       gp.data_nascimento,gp.nome_mae,
       gp.fontes_distintas,gp.estado_concordancia,gp.estado_identidade,
       gp.completude_nucleo,gp.atualizado_em
FROM gold.pessoa gp
JOIN serving.v_pessoa_nome_referencia nr ON nr.pessoa_uuid=gp.pessoa_uuid;
GO
