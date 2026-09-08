-- Identidade progressiva V1 - projeção Serving/BI.
-- Não ativa Linkage probabilístico, não altera vínculos e não recompõe fatos.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('identidade.pessoa_origem_progressiva','U') IS NULL
   OR OBJECT_ID('silver.pessoa_origem','U') IS NULL
   OR OBJECT_ID('ref.sistema_origem','U') IS NULL
   OR OBJECT_ID('ref.gestor','U') IS NULL
    THROW 51370,'Identidade progressiva e referências de origem devem estar instaladas antes da projeção Serving.',1;
GO
CREATE OR ALTER VIEW serving.v_identidade_origem_progressiva
AS
SELECT
    p.pessoa_origem_id,
    g.codigo AS gestor_codigo,
    so.codigo AS sistema_origem_codigo,
    o.codigo_pessoa_origem,
    p.initial_uuid,
    p.canonical_uuid,
    p.estado,
    p.versao,
    CAST(CASE WHEN p.estado='REFERENCIA' AND p.canonical_uuid IS NOT NULL THEN 1 ELSE 0 END AS bit) AS apta_contagem_pessoa_canonica,
    p.criado_em,
    p.atualizado_em,
    p.ultima_resolucao_em
FROM identidade.pessoa_origem_progressiva p
JOIN silver.pessoa_origem o ON o.pessoa_origem_id=p.pessoa_origem_id
JOIN ref.sistema_origem so ON so.sistema_origem_id=o.sistema_origem_id
JOIN ref.gestor g ON g.gestor_id=so.gestor_id;
GO
