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
  finalizado_em,registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,
  fresh_pending,reavaliados
 FROM identidade.linkage_run ORDER BY iniciado_em DESC,linkage_run_id DESC
)
SELECT CONVERT(varchar(36),r.linkage_run_id) AS run_id,
 r.tipo_run,r.status,r.modelo_versao,r.iniciado_em,r.finalizado_em,
 DATEDIFF_BIG(MILLISECOND,r.iniciado_em,r.finalizado_em) AS duracao_ms,
 r.registros_elegiveis,r.avaliados,r.resolvidos,r.nao_resolvidos,r.conflitos,
 ISNULL(l.gravadas,CONVERT(bigint,0)) AS linhas_gravadas,
 r.fresh_pending AS fresh_pending_exato,
 r.reavaliados AS reavaliados_exato,
 CAST(100.0*r.reavaliados/NULLIF(r.registros_elegiveis,0) AS DECIMAL(9,2))
   AS percentual_reavaliados_elegiveis
FROM recent r
OUTER APPLY (
 SELECT COUNT_BIG(*) AS gravadas
 FROM identidade.linkage_resultado lr WHERE lr.linkage_run_id=r.linkage_run_id
) l ORDER BY r.iniciado_em DESC,r.linkage_run_id DESC;
-- calculado_em e DATETIMEOFFSET: converta o instante para UTC ANTES de extrair
-- a data, para que viradas de meia-noite em offsets distintos caiam no mesmo dia.
;WITH utc_dates AS (
 SELECT CONVERT(date,SWITCHOFFSET(calculado_em,'+00:00')) AS data_utc
 FROM identidade.linkage_resultado
)
SELECT data_utc,COUNT_BIG(*) AS linhas_gravadas
FROM utc_dates
GROUP BY data_utc ORDER BY data_utc DESC;
-- Semanas UTC iniciadas na segunda-feira, independentes de DATEFIRST/idioma.
-- Comparar somente semanas contiguas; lacunas nao representam crescimento zero.
;WITH utc_dates AS (
 SELECT CONVERT(date,SWITCHOFFSET(calculado_em,'+00:00')) AS data_utc
 FROM identidade.linkage_resultado
), weekly AS (
 SELECT DATEADD(DAY,-(DATEDIFF(DAY,CONVERT(date,'19000101',112),
   data_utc)%7),data_utc) AS semana_inicio_utc,
   COUNT_BIG(*) AS linhas_gravadas
 FROM utc_dates
 GROUP BY DATEADD(DAY,-(DATEDIFF(DAY,CONVERT(date,'19000101',112),
   data_utc)%7),data_utc)
), compared AS (
 SELECT semana_inicio_utc,linhas_gravadas,
  LAG(semana_inicio_utc) OVER (ORDER BY semana_inicio_utc) AS semana_anterior_utc,
  LAG(linhas_gravadas) OVER (ORDER BY semana_inicio_utc) AS linhas_semana_anterior
 FROM weekly
)
SELECT TOP (16) semana_inicio_utc,linhas_gravadas,
 CASE WHEN DATEDIFF(DAY,semana_anterior_utc,semana_inicio_utc)=7
   THEN linhas_semana_anterior ELSE NULL END AS linhas_semana_anterior_contigua,
 CASE WHEN DATEDIFF(DAY,semana_anterior_utc,semana_inicio_utc)=7
   THEN linhas_gravadas-linhas_semana_anterior ELSE NULL END AS diferenca_vs_semana_anterior,
 CASE WHEN DATEDIFF(DAY,semana_anterior_utc,semana_inicio_utc)=7
   AND linhas_semana_anterior>0
   THEN CAST(100.0*(linhas_gravadas-linhas_semana_anterior)
     /linhas_semana_anterior AS decimal(18,2)) ELSE NULL END AS variacao_percentual_contigua
FROM compared ORDER BY semana_inicio_utc DESC;
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
