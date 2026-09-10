-- Projeção read-only do histórico de composição efetivamente publicado.
-- Não escolhe sucessor canônico, não altera identidade, Gold/Serving factual ou Linkage.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('identidade.composicao_historico_aplicado','U') IS NULL OR
   OBJECT_ID('identidade.composicao_publicacao','U') IS NULL
    THROW 51620,'Histórico aplicado e publicação de composição devem estar instalados.',1;
GO
CREATE OR ALTER VIEW serving.v_identidade_composicao_historico_publicado
AS
SELECT
    h.decision_id,
    h.reference_uuid,
    CONVERT(uniqueidentifier,j.[value]) AS member_uuid,
    h.registrado_em,
    p.published_at,
    p.publication_version
FROM identidade.composicao_historico_aplicado h
JOIN identidade.composicao_publicacao p
  ON p.decision_id=h.decision_id
 AND p.state=N'PUBLICADA'
CROSS APPLY OPENJSON(h.members_json) j;
GO
