/* DT-13: retirar o flag; substituir o painel por vazão horária sem modo operacional. */
SET XACT_ABORT ON;
GO
DROP VIEW IF EXISTS serving.v_bi_carga_inicial;
DROP TABLE IF EXISTS controle.modo_carga_inicial;
GO
CREATE OR ALTER VIEW serving.v_bi_processamento_hora AS
WITH serie AS (
 SELECT DATEADD(HOUR,DATEDIFF(HOUR,CONVERT(datetime2(0),'20000101'),CONVERT(datetime2(0),SWITCHOFFSET(ip.processado_em,'+00:00'))),CONVERT(datetime2(0),'20000101')) hora_utc,SUM(CASE WHEN ip.classe_item='PESSOA' THEN CAST(1 AS BIGINT) ELSE 0 END) pessoas FROM ingestao.item_processado ip WHERE ip.consolidado_em IS NULL GROUP BY DATEADD(HOUR,DATEDIFF(HOUR,CONVERT(datetime2(0),'20000101'),CONVERT(datetime2(0),SWITCHOFFSET(ip.processado_em,'+00:00'))),CONVERT(datetime2(0),'20000101'))
 UNION ALL SELECT processado_hora_utc,SUM(CASE WHEN classe_item='PESSOA' THEN quantidade ELSE 0 END) FROM ingestao.item_processado_resumo GROUP BY processado_hora_utc), agg AS (SELECT hora_utc,SUM(pessoas) pessoas_processadas FROM serie GROUP BY hora_utc)
SELECT a.hora_utc,a.pessoas_processadas,CAST(a.pessoas_processadas AS DECIMAL(18,2)) pessoas_por_hora FROM agg a;
GO
