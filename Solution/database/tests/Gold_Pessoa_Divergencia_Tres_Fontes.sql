-- Regressão de leitura: executar APENAS após criar três observações sintéticas
-- de três gestores associadas ao mesmo UUID, em banco descartável.
-- O script não insere nem altera observações; falha se a fixture não existir.
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @pessoa_uuid UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001'; -- substituir
IF @pessoa_uuid='00000000-0000-0000-0000-000000000001'
 THROW 51000, 'Substituir @pessoa_uuid pelo UUID da fixture sintética.', 1;
EXEC identidade.sp_recompor_gold_pessoa @pessoa_uuid;
;WITH obs_ids AS (
 SELECT po.pessoa_observacao_id FROM silver.pessoa_observacao po
 JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
 WHERE vc.pessoa_uuid=@pessoa_uuid AND vc.status='RESOLVIDO'
 UNION
 SELECT po.pessoa_observacao_id FROM silver.pessoa_observacao po
 JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
 WHERE p.estado='REFERENCIA' AND p.canonical_uuid=@pessoa_uuid
 UNION
 SELECT po.pessoa_observacao_id FROM silver.pessoa_observacao po
 JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
 WHERE p.initial_uuid=@pessoa_uuid AND p.estado IN ('PROVISORIA','INDEFINIDA')
), evidencias AS (
 SELECT po.pessoa_observacao_id,po.gestor_id,po.nome_cmp,po.nome_completo
 FROM obs_ids i JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=i.pessoa_observacao_id
)
SELECT pessoa_observacao_id,gestor_id,nome_completo,nome_cmp FROM evidencias
ORDER BY gestor_id,pessoa_observacao_id;
IF NOT EXISTS (SELECT 1 FROM gold.pessoa
 WHERE pessoa_uuid=@pessoa_uuid AND estado_concordancia='DIVERGENTE')
 THROW 51001,'Gold nao sinalizou divergencia.',1;
;WITH obs_ids AS (
 SELECT po.pessoa_observacao_id FROM silver.pessoa_observacao po
 JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
 WHERE vc.pessoa_uuid=@pessoa_uuid AND vc.status='RESOLVIDO'
 UNION
 SELECT po.pessoa_observacao_id FROM silver.pessoa_observacao po
 JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
 WHERE p.estado='REFERENCIA' AND p.canonical_uuid=@pessoa_uuid
 UNION
 SELECT po.pessoa_observacao_id FROM silver.pessoa_observacao po
 JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
 WHERE p.initial_uuid=@pessoa_uuid AND p.estado IN ('PROVISORIA','INDEFINIDA')
)
SELECT po.pessoa_observacao_id,po.gestor_id,po.nome_cmp INTO #evidencias
FROM obs_ids i JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=i.pessoa_observacao_id;
IF (SELECT COUNT(DISTINCT gestor_id) FROM #evidencias)<3
 OR (SELECT COUNT(DISTINCT nome_cmp) FROM #evidencias)<3
 THROW 51002,'Nao ha tres gestores e tres valores distintos consultaveis.',1;
DECLARE @antes INT=(SELECT COUNT(*) FROM #evidencias);
EXEC identidade.sp_recompor_gold_pessoa @pessoa_uuid;
IF (SELECT COUNT(*) FROM silver.pessoa_observacao po JOIN #evidencias e
 ON e.pessoa_observacao_id=po.pessoa_observacao_id)<>@antes
 THROW 51003,'Observacao original nao encontrada apos recomposicao.',1;
DROP TABLE #evidencias;
PRINT 'PASS: tres fontes discordantes preservadas e consultaveis apos recomposicao repetida.';
