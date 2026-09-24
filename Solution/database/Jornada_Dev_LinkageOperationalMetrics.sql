-- Diagnostico agregado de Linkage (#424). Somente SELECT; nenhuma alteracao operacional.
SET NOCOUNT ON;
SET LOCK_TIMEOUT 5000;
DECLARE @profile nvarchar(32)=ISNULL(CONVERT(nvarchar(32),
 (SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.EnvironmentProfile')),N'');
IF @profile NOT IN (N'Development',N'Homologation',N'HML')
 THROW 51880,'Diagnostico disponivel apenas em DEV/HML.',1;
IF OBJECT_ID(N'identidade.linkage_run',N'U') IS NULL
 OR OBJECT_ID(N'identidade.linkage_resultado',N'U') IS NULL
 THROW 51881,'Tabelas de Linkage ausentes.',1;
SELECT DB_NAME() AS banco,@profile AS perfil,SYSUTCDATETIME() AS coletado_utc;
SELECT metodo_resolucao,status,COUNT_BIG(*) AS observacoes
FROM identidade.v_vinculo_corrente
WHERE metodo_resolucao IN (N'PENDENTE_PROBABILISTICO',N'LINKAGE_PROBABILISTICO')
GROUP BY metodo_resolucao,status ORDER BY metodo_resolucao,status;
;WITH recent AS (
 SELECT TOP (25) linkage_run_id,tipo_run,status,modelo_versao,iniciado_em,
  finalizado_em,registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos
 FROM identidade.linkage_run ORDER BY iniciado_em DESC,linkage_run_id DESC
)
SELECT CONVERT(varchar(36),r.linkage_run_id) AS run_id,
 r.tipo_run,r.status,r.modelo_versao,r.iniciado_em,r.finalizado_em,
 DATEDIFF_BIG(MILLISECOND,r.iniciado_em,r.finalizado_em) AS duracao_ms,
 r.registros_elegiveis,r.avaliados,r.resolvidos,r.nao_resolvidos,r.conflitos,
 ISNULL(l.gravadas,CONVERT(bigint,0)) AS linhas_gravadas,
 CAST(NULL AS bigint) AS fresh_pending_exato_nao_persistido,
 CAST(NULL AS bigint) AS reavaliados_exato_nao_persistido
FROM recent r
OUTER APPLY (
 SELECT COUNT_BIG(*) AS gravadas
 FROM identidade.linkage_resultado lr WHERE lr.linkage_run_id=r.linkage_run_id
) l ORDER BY r.iniciado_em DESC,r.linkage_run_id DESC;
SELECT CONVERT(date,calculado_em) AS data_utc,COUNT_BIG(*) AS linhas_gravadas
FROM identidade.linkage_resultado
GROUP BY CONVERT(date,calculado_em) ORDER BY data_utc DESC;
;WITH counts AS (
 SELECT pessoa_observacao_id,COUNT_BIG(*) AS avaliacoes
 FROM identidade.linkage_resultado GROUP BY pessoa_observacao_id
)
SELECT CASE WHEN avaliacoes=1 THEN N'1' WHEN avaliacoes=2 THEN N'2'
 WHEN avaliacoes BETWEEN 3 AND 5 THEN N'3_A_5'
 WHEN avaliacoes BETWEEN 6 AND 10 THEN N'6_A_10' ELSE N'11_MAIS' END AS faixa,
 COUNT_BIG(*) AS observacoes,SUM(avaliacoes) AS linhas
FROM counts
GROUP BY CASE WHEN avaliacoes=1 THEN N'1' WHEN avaliacoes=2 THEN N'2'
 WHEN avaliacoes BETWEEN 3 AND 5 THEN N'3_A_5'
 WHEN avaliacoes BETWEEN 6 AND 10 THEN N'6_A_10' ELSE N'11_MAIS' END;
SELECT SUM(row_count) AS linhas_atuais,
 CAST(SUM(reserved_page_count)*8.0/1024 AS decimal(18,2)) AS reservado_mb,
 CAST(SUM(used_page_count)*8.0/1024 AS decimal(18,2)) AS utilizado_mb
FROM sys.dm_db_partition_stats
WHERE object_id=OBJECT_ID(N'identidade.linkage_resultado') AND index_id IN (0,1);
