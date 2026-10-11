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

-- Mapeamento read-only das ocorrencias candidatas para a identidade Gold.
-- Usa as mesmas tres origens de UUID da recomposicao de gold.pessoa.
-- Se uma observacao aponta para UUIDs diferentes, abstencao: nao publica
-- candidato ambiguo nem escolhe vencedor. Uma observacao pode ter N documentos.
-- Esta view NAO deduplica documentos nem altera Linkage/Gold; a identidade
-- documental por tipo/qualificadores e a proveniencia requerem consolidador.
CREATE OR ALTER VIEW gold.vw_documento_evidencia_candidato_uuid
AS
WITH mapeamentos AS (
    SELECT vc.pessoa_observacao_id, vc.pessoa_uuid
    FROM identidade.v_vinculo_corrente vc
    WHERE vc.status=N'RESOLVIDO' AND vc.pessoa_uuid IS NOT NULL

    UNION

    SELECT po.pessoa_observacao_id, p.canonical_uuid AS pessoa_uuid
    FROM silver.pessoa_observacao po
    JOIN identidade.pessoa_origem_progressiva p
      ON p.pessoa_origem_id=po.pessoa_origem_id
    WHERE p.estado=N'REFERENCIA' AND p.canonical_uuid IS NOT NULL

    UNION

    SELECT po.pessoa_observacao_id, p.initial_uuid AS pessoa_uuid
    FROM silver.pessoa_observacao po
    JOIN identidade.pessoa_origem_progressiva p
      ON p.pessoa_origem_id=po.pessoa_origem_id
    WHERE p.estado IN(N'PROVISORIA',N'INDEFINIDA')
      AND p.initial_uuid IS NOT NULL
), mapeamentos_sem_ambiguidade AS (
    SELECT m.pessoa_observacao_id, m.pessoa_uuid
    FROM mapeamentos m
    WHERE NOT EXISTS (
        SELECT 1 FROM mapeamentos outro
        WHERE outro.pessoa_observacao_id=m.pessoa_observacao_id
          AND outro.pessoa_uuid<>m.pessoa_uuid
    )
)
SELECT m.pessoa_uuid, c.pessoa_observacao_id, c.tipo_documento_codigo,
       c.documento_evidencia_id, c.modelo_documento_id,
       c.emissor_codigo, c.uf_emissor, c.classe_evidencia,
       c.data_evidencia, c.base_origem_codigo, c.ocorrencia_origem_codigo,
       c.atributo_codigo, c.valor_original
FROM silver.vw_documento_evidencia_candidato_gold c
JOIN mapeamentos_sem_ambiguidade m
  ON m.pessoa_observacao_id=c.pessoa_observacao_id
JOIN gold.pessoa gp ON gp.pessoa_uuid=m.pessoa_uuid;
GO
