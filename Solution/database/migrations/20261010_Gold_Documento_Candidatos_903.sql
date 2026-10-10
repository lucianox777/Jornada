SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- #903 fase 3: candidatos documentais por atributo, sem substituir data propria.
-- Nao atualiza Gold automaticamente: ausencia de data/empate exigem abstencao
-- ate existir decisao governada de desempate e destino de proveniencia.
CREATE OR ALTER VIEW silver.vw_documento_evidencia_candidato_gold
AS
WITH admissiveis AS (
 SELECT e.pessoa_observacao_id,
        v.atributo_codigo, v.valor_original,
        e.documento_evidencia_id, e.modelo_documento_id,
        e.classe_evidencia, e.data_evidencia,
        e.base_origem_codigo, e.ocorrencia_origem_codigo,
        ROW_NUMBER() OVER (
          PARTITION BY e.pessoa_observacao_id,v.atributo_codigo
          ORDER BY CASE WHEN e.classe_evidencia=N'DOCUMENTO' THEN 0 ELSE 1 END,
                   CASE WHEN e.data_evidencia IS NULL THEN 1 ELSE 0 END,
                   e.data_evidencia DESC, e.documento_evidencia_id
        ) AS ordem_candidata,
        COUNT(*) OVER (
          PARTITION BY e.pessoa_observacao_id,v.atributo_codigo,
                       e.classe_evidencia,e.data_evidencia
        ) AS quantidade_mesma_classe_data,
        MAX(CASE WHEN e.data_evidencia IS NULL THEN 1 ELSE 0 END) OVER (
          PARTITION BY e.pessoa_observacao_id,v.atributo_codigo,e.classe_evidencia
        ) AS classe_possui_data_ausente
 FROM silver.documento_evidencia_valor v
 JOIN silver.documento_evidencia_observacao e
   ON e.documento_evidencia_id=v.documento_evidencia_id
  AND e.modelo_documento_id=v.modelo_documento_id
 JOIN ref.modelo_documento m ON m.modelo_documento_id=e.modelo_documento_id
 JOIN ref.modelo_documento_atributo a
   ON a.modelo_documento_id=v.modelo_documento_id
  AND a.atributo_codigo=v.atributo_codigo
 WHERE e.status_validacao=N'VALIDO'
   AND m.estado=N'PUBLICADO'
   AND a.comprovacao_admissivel=1
)
SELECT pessoa_observacao_id,atributo_codigo,valor_original,
       documento_evidencia_id,modelo_documento_id,classe_evidencia,
       data_evidencia,base_origem_codigo,ocorrencia_origem_codigo,
       ordem_candidata,quantidade_mesma_classe_data,
       CASE WHEN classe_possui_data_ausente=1 THEN N'ABSTER_SEM_DATA'
            WHEN quantidade_mesma_classe_data>1 THEN N'ABSTER_EMPATE'
            ELSE N'CANDIDATO_DATADO' END AS situacao_candidata
FROM admissiveis;
GO
