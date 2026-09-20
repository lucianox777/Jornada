SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Nome de referência no serving
  -----------------------------
  Referência de apresentação e evidência de identidade são conceitos independentes.

  - gold.pessoa.nome_completo continua sendo dado interno do núcleo Golden e não é
    substituído por nome social.
  - NOME_SOCIAL corrente, quando existir, tem precedência de apresentação sobre o
    nome civil independentemente de estar DECLARADO ou COMPROVADO.
  - a seleção é derivada, rastreável e reversível: a origem da referência fica explícita.
  - nenhuma coluna nome_referencia é criada em gold/identidade e o linkage não passa
    a consumir esta view.
*/

IF OBJECT_ID(N'gold.pessoa',N'U') IS NULL
   OR OBJECT_ID(N'silver.pessoa_atributo_observacao',N'U') IS NULL
   OR OBJECT_ID(N'identidade.v_vinculo_corrente',N'V') IS NULL
    THROW 51840,'Nome de referência exige Gold, atributos Silver e vínculo corrente instalados.',1;
GO

CREATE OR ALTER VIEW serving.v_pessoa_nome_referencia AS
SELECT gp.pessoa_uuid,
       COALESCE(ns.valor,gp.nome_completo) AS nome_referencia,
       CASE WHEN ns.pessoa_atributo_observacao_id IS NULL THEN N'NOME_CIVIL' ELSE N'NOME_SOCIAL' END AS nome_referencia_tipo,
       CASE
         WHEN ns.pessoa_atributo_observacao_id IS NULL THEN N'GOLD_PESSOA'
         ELSE N'SILVER_PESSOA_ATRIBUTO_OBSERVACAO'
       END AS nome_referencia_fonte_tipo,
       ns.pessoa_atributo_observacao_id AS nome_referencia_fonte_observacao_id,
       ns.fonte_gestor_id AS nome_referencia_fonte_gestor_id,
       ns.source_record_id AS nome_referencia_source_record_id,
       CASE
         WHEN ns.pessoa_atributo_observacao_id IS NULL THEN gp.atualizado_em
         ELSE ns.precedencia_em
       END AS nome_referencia_em
FROM gold.pessoa gp
OUTER APPLY(
    SELECT TOP(1)
           pa.pessoa_atributo_observacao_id,
           pa.valor,
           pa.fonte_gestor_id,
           pa.source_record_id,
           COALESCE(pa.referencia_evidencia,pa.verificado_em,pa.atualizado_em_origem,pa.ingested_at) AS precedencia_em
    FROM silver.pessoa_atributo_observacao pa
    JOIN identidade.v_vinculo_corrente vc
      ON vc.pessoa_observacao_id=pa.pessoa_observacao_id
     AND vc.status=N'RESOLVIDO'
     AND vc.pessoa_uuid=gp.pessoa_uuid
    WHERE pa.atributo_codigo=N'NOME_SOCIAL'
      AND pa.status_evidencia IN(N'DECLARADO',N'COMPROVADO')
      AND NULLIF(LTRIM(RTRIM(pa.valor)),N'') IS NOT NULL
    ORDER BY COALESCE(pa.referencia_evidencia,pa.verificado_em,pa.atualizado_em_origem,pa.ingested_at) DESC,
             pa.pessoa_atributo_observacao_id DESC
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
