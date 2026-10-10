SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- #903: exposicao read-only de todas as ocorrencias documentais admissiveis.
-- Nao elege documento vencedor por data/classe e nao deduplica por observacao.
-- Deduplicacao 1:N por UUID e proveniencia serao tratadas no consolidador Gold.
CREATE OR ALTER VIEW silver.vw_documento_evidencia_candidato_gold
AS
SELECT e.pessoa_observacao_id,
       m.tipo_documento_codigo,
       e.documento_evidencia_id,
       e.modelo_documento_id,
       e.emissor_codigo,
       e.uf_emissor,
       e.classe_evidencia,
       e.data_evidencia,
       e.base_origem_codigo,
       e.ocorrencia_origem_codigo,
       v.atributo_codigo,
       v.valor_original
FROM silver.documento_evidencia_observacao e
JOIN silver.documento_evidencia_valor v
  ON v.documento_evidencia_id=e.documento_evidencia_id
 AND v.modelo_documento_id=e.modelo_documento_id
JOIN ref.modelo_documento m ON m.modelo_documento_id=e.modelo_documento_id
JOIN ref.modelo_documento_atributo a
  ON a.modelo_documento_id=v.modelo_documento_id
 AND a.atributo_codigo=v.atributo_codigo
WHERE e.status_validacao=N'VALIDO'
  AND m.estado=N'PUBLICADO'
  AND a.comprovacao_admissivel=1;
GO
