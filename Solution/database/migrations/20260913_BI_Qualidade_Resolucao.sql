SET XACT_ABORT ON;
GO

/*
Qualidade da resolucao de identidade.

Separa deliberadamente:
1. indicadores operacionais observaveis (CONFLITO/NAO_RESOLVIDO/taxa de resolucao);
2. estimativas calibradas de erro (PPV/sensibilidade e IC), que so podem ser
   publicadas quando houver corpus de referencia e calibracao governada suficientes.

Os indicadores operacionais NAO sao taxas de erro de linkage.
*/

IF OBJECT_ID('identidade.linkage_quality_estimate','U') IS NULL
BEGIN
    CREATE TABLE identidade.linkage_quality_estimate(
        linkage_quality_estimate_id BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT pk_identidade_linkage_quality_estimate PRIMARY KEY,
        linkage_run_id UNIQUEIDENTIFIER NOT NULL,
        estrato NVARCHAR(40) NOT NULL,
        corpus_referencia_codigo NVARCHAR(160) NOT NULL,
        calibracao_versao_codigo NVARCHAR(120) NOT NULL,
        metodo_estimacao NVARCHAR(120) NULL,
        tamanho_amostra_referencia BIGINT NOT NULL,
        ppv_estimado DECIMAL(12,10) NOT NULL,
        ppv_ic_inferior DECIMAL(12,10) NOT NULL,
        ppv_ic_superior DECIMAL(12,10) NOT NULL,
        sensibilidade_estimada DECIMAL(12,10) NOT NULL,
        sensibilidade_ic_inferior DECIMAL(12,10) NOT NULL,
        sensibilidade_ic_superior DECIMAL(12,10) NOT NULL,
        medido_em DATETIMEOFFSET(7) NOT NULL,
        criado_em DATETIMEOFFSET(7) NOT NULL
            CONSTRAINT df_identidade_linkage_quality_estimate_criado_em DEFAULT(SYSDATETIMEOFFSET()),
        CONSTRAINT fk_identidade_linkage_quality_estimate_run
            FOREIGN KEY(linkage_run_id) REFERENCES identidade.linkage_run(linkage_run_id),
        CONSTRAINT ck_identidade_linkage_quality_estimate_estrato
            CHECK(estrato IN('GERAL','CPF','CNS_SEM_CPF','SEM_IDENTIFICADOR_FORTE')),
        CONSTRAINT ck_identidade_linkage_quality_estimate_amostra
            CHECK(tamanho_amostra_referencia > 0),
        CONSTRAINT ck_identidade_linkage_quality_estimate_ppv
            CHECK(ppv_estimado BETWEEN 0 AND 1
              AND ppv_ic_inferior BETWEEN 0 AND 1
              AND ppv_ic_superior BETWEEN 0 AND 1
              AND ppv_ic_inferior <= ppv_estimado
              AND ppv_estimado <= ppv_ic_superior),
        CONSTRAINT ck_identidade_linkage_quality_estimate_sensibilidade
            CHECK(sensibilidade_estimada BETWEEN 0 AND 1
              AND sensibilidade_ic_inferior BETWEEN 0 AND 1
              AND sensibilidade_ic_superior BETWEEN 0 AND 1
              AND sensibilidade_ic_inferior <= sensibilidade_estimada
              AND sensibilidade_estimada <= sensibilidade_ic_superior),
        CONSTRAINT uq_identidade_linkage_quality_estimate
            UNIQUE(linkage_run_id,estrato,corpus_referencia_codigo,calibracao_versao_codigo)
    );
END;
GO

CREATE OR ALTER VIEW serving.v_bi_qualidade_resolucao_operacional AS
SELECT
    lr.linkage_run_id,
    lr.modelo_id,
    lr.modelo_versao,
    ml.algoritmo_versao,
    lr.tipo_run,
    lr.status,
    lr.gestor_codigo_filtro,
    lr.iniciado_em,
    lr.finalizado_em,
    lr.avaliados,
    lr.resolvidos,
    lr.nao_resolvidos,
    lr.conflitos,
    lr.sem_candidato_no_bloco,
    CAST(CASE WHEN lr.avaliados > 0 THEN 1.0 * lr.resolvidos / lr.avaliados END AS DECIMAL(12,10)) AS taxa_resolucao,
    CAST(CASE WHEN lr.avaliados > 0 THEN 1.0 * lr.conflitos / lr.avaliados END AS DECIMAL(12,10)) AS proporcao_conflito,
    CAST(CASE WHEN lr.avaliados > 0 THEN 1.0 * lr.nao_resolvidos / lr.avaliados END AS DECIMAL(12,10)) AS proporcao_nao_resolvido,
    CAST(CASE WHEN lr.avaliados > 0 THEN 1.0 * lr.sem_candidato_no_bloco / lr.avaliados END AS DECIMAL(12,10)) AS proporcao_sem_candidato_no_bloco,
    CAST('INDICADOR_OPERACIONAL_NAO_E_TAXA_DE_ERRO' AS NVARCHAR(80)) AS classe_metrica
FROM identidade.linkage_run lr
LEFT JOIN identidade.modelo_linkage ml ON ml.modelo_id = lr.modelo_id;
GO

CREATE OR ALTER VIEW serving.v_bi_qualidade_resolucao_calibrada AS
SELECT
    q.linkage_quality_estimate_id,
    q.linkage_run_id,
    lr.modelo_id,
    lr.modelo_versao,
    ml.algoritmo_versao,
    q.estrato,
    q.corpus_referencia_codigo,
    q.calibracao_versao_codigo,
    q.metodo_estimacao,
    q.tamanho_amostra_referencia,
    q.ppv_estimado,
    q.ppv_ic_inferior,
    q.ppv_ic_superior,
    CAST(1.0 - q.ppv_estimado AS DECIMAL(12,10)) AS taxa_falso_vinculo_estimada,
    q.sensibilidade_estimada,
    q.sensibilidade_ic_inferior,
    q.sensibilidade_ic_superior,
    CAST(1.0 - q.sensibilidade_estimada AS DECIMAL(12,10)) AS taxa_vinculo_perdido_estimada,
    q.medido_em,
    q.criado_em,
    CAST('METRICA_CALIBRADA_COM_CORPUS_REFERENCIA' AS NVARCHAR(80)) AS classe_metrica
FROM identidade.linkage_quality_estimate q
JOIN identidade.linkage_run lr ON lr.linkage_run_id = q.linkage_run_id
LEFT JOIN identidade.modelo_linkage ml ON ml.modelo_id = lr.modelo_id;
GO
