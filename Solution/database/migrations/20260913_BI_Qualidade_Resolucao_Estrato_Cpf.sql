SET XACT_ABORT ON;
GO

/*
Estrato operacional observavel da qualidade da resolucao.

A Silver corrente materializa CPF no nucleo de pessoa_observacao, mas ainda nao
materializa CNS/NIS por observacao. Portanto este corte nao inventa
CNS_SEM_CPF: publica somente CPF e SEM_CPF, que sao classificacoes diretamente
observaveis hoje. Quando identificadores multiplos forem materializados na
Silver, o estrato pode ser refinado sem reinterpretar historico.
*/
CREATE OR ALTER VIEW serving.v_bi_qualidade_resolucao_operacional_estrato AS
WITH base AS (
    SELECT
        lr.linkage_run_id,
        lr.modelo_id,
        lr.modelo_versao,
        ml.algoritmo_versao,
        lr.tipo_run,
        lr.status AS status_run,
        lr.iniciado_em,
        lr.finalizado_em,
        CASE WHEN po.cpf IS NOT NULL THEN 'CPF' ELSE 'SEM_CPF' END AS estrato_identificacao,
        r.status AS status_resultado,
        r.motivo
    FROM identidade.linkage_resultado r
    JOIN identidade.linkage_run lr
      ON lr.linkage_run_id=r.linkage_run_id
    LEFT JOIN identidade.modelo_linkage ml
      ON ml.modelo_id=lr.modelo_id
    JOIN silver.pessoa_observacao po
      ON po.pessoa_observacao_id=r.pessoa_observacao_id
)
SELECT
    linkage_run_id,
    modelo_id,
    modelo_versao,
    algoritmo_versao,
    tipo_run,
    status_run,
    iniciado_em,
    finalizado_em,
    estrato_identificacao,
    COUNT_BIG(*) AS avaliados,
    SUM(CASE WHEN status_resultado='RESOLVIDO' THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END) AS resolvidos,
    SUM(CASE WHEN status_resultado='NAO_RESOLVIDO' THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END) AS nao_resolvidos,
    SUM(CASE WHEN status_resultado='CONFLITO' THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END) AS conflitos,
    SUM(CASE WHEN status_resultado='NAO_RESOLVIDO' AND motivo='SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO'
             THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END) AS sem_candidato_no_bloco,
    CAST(1.0 * SUM(CASE WHEN status_resultado='RESOLVIDO' THEN 1 ELSE 0 END) / NULLIF(COUNT_BIG(*),0) AS DECIMAL(12,10)) AS taxa_resolucao,
    CAST(1.0 * SUM(CASE WHEN status_resultado='CONFLITO' THEN 1 ELSE 0 END) / NULLIF(COUNT_BIG(*),0) AS DECIMAL(12,10)) AS proporcao_conflito,
    CAST(1.0 * SUM(CASE WHEN status_resultado='NAO_RESOLVIDO' THEN 1 ELSE 0 END) / NULLIF(COUNT_BIG(*),0) AS DECIMAL(12,10)) AS proporcao_nao_resolvido,
    CAST('INDICADOR_OPERACIONAL_NAO_E_TAXA_DE_ERRO' AS NVARCHAR(80)) AS classe_metrica
FROM base
GROUP BY
    linkage_run_id,modelo_id,modelo_versao,algoritmo_versao,tipo_run,status_run,
    iniciado_em,finalizado_em,estrato_identificacao;
GO
