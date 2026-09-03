/* Jornada Fase 1 v3.53 - consultas somente-leitura para HML.
   Não executa REBUILD/REORGANIZE nem altera configuração do servidor. */
SET NOCOUNT ON;

-- Esperas de trava acumuladas desde o último restart.
SELECT wait_type, waiting_tasks_count, wait_time_ms, max_wait_time_ms
FROM sys.dm_os_wait_stats
WHERE wait_type LIKE 'LCK%'
ORDER BY wait_time_ms DESC;

-- Requisições atualmente bloqueadas.
SELECT r.session_id,r.blocking_session_id,r.status,r.wait_type,r.wait_time,r.command,
       DB_NAME(r.database_id) database_name,t.text sql_text
FROM sys.dm_exec_requests r
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
WHERE r.blocking_session_id<>0 OR r.wait_type LIKE 'LCK%';

-- Tamanho dos principais objetos de ingestão.
SELECT s.name schema_name,o.name object_name,SUM(ps.row_count) row_count,
       CAST(SUM(ps.reserved_page_count)*8.0/1024 AS decimal(18,2)) reserved_mb
FROM sys.dm_db_partition_stats ps
JOIN sys.objects o ON o.object_id=ps.object_id
JOIN sys.schemas s ON s.schema_id=o.schema_id
WHERE (s.name='ingestao' AND o.name IN('item_processado','item_processado_resumo','lote','entrega'))
   OR (s.name='gold' AND o.name='pessoa')
GROUP BY s.name,o.name ORDER BY reserved_mb DESC;

-- Estatísticas/fragmentação: diagnóstico, não gatilho automático de manutenção.
SELECT OBJECT_SCHEMA_NAME(ips.object_id) schema_name,OBJECT_NAME(ips.object_id) table_name,
       i.name index_name,ips.page_count,ips.avg_fragmentation_in_percent,ips.avg_page_space_used_in_percent
FROM sys.dm_db_index_physical_stats(DB_ID(),NULL,NULL,NULL,'SAMPLED') ips
JOIN sys.indexes i ON i.object_id=ips.object_id AND i.index_id=ips.index_id
WHERE ips.page_count>=1000
ORDER BY ips.page_count DESC;

-- Carga inicial / backlog.
SELECT * FROM serving.v_bi_carga_inicial ORDER BY hora_utc DESC;
SELECT status,COUNT_BIG(*) lotes,MIN(criado_em) lote_mais_antigo
FROM ingestao.lote GROUP BY status ORDER BY status;

-- Manutenção da Bronze.
SELECT TOP(50) * FROM serving.v_bi_manutencao_bronze ORDER BY finalizado_em DESC;

-- v3.39: calibrar alerta de idade do órfão mais antigo pela view serving.v_bi_manutencao_bronze.
SELECT TOP(100) * FROM serving.v_bi_manutencao_bronze ORDER BY ciclo_id DESC;

-- A latência da persistência da auditoria é emitida pela API como campo estruturado AuditPersistenceMs; coletar p50/p95/p99 no stack de logs/APM de HML.

-- v3.53: watchdog do pipeline - consultas somente leitura equivalentes aos alertas do worker.
-- O watchdog NÃO agenda, cancela, altera status, libera lock ou encerra sessão.
DECLARE @watchdog_agora DATETIMEOFFSET(7)=SYSUTCDATETIME();

-- Runs de linkage potencialmente estagnados. Aplicar o limite homologado de PipelineWatchdog:LinkageRunMaxMinutes.
SELECT linkage_run_id,modelo_versao,tipo_run,status,iniciado_em,
       DATEDIFF(MINUTE,iniciado_em,@watchdog_agora) idade_minutos,avaliados,registros_elegiveis
FROM identidade.linkage_run
WHERE status IN('PREPARANDO','EXECUTANDO')
ORDER BY iniciado_em;

-- Geração de modelo potencialmente estagnada. Aplicar PipelineWatchdog:ModelGenerationMaxMinutes.
SELECT modelo_id,versao,status,gerado_em,
       DATEDIFF(MINUTE,gerado_em,@watchdog_agora) idade_minutos,registros_lidos,pessoas_unicas,falha_resumo
FROM identidade.modelo_linkage
WHERE status='GERANDO'
ORDER BY gerado_em;

-- Leases de Processor vencidos. A recuperação é responsabilidade do próprio Processor; o watchdog só alerta.
SELECT lote_id,entrega_id,status,lease_owner,lease_adquirido_em,heartbeat_em,lease_expira_em,
       DATEDIFF(MINUTE,lease_expira_em,@watchdog_agora) minutos_desde_expiracao,tentativa_count,recuperacao_count
FROM ingestao.lote
WHERE status IN('VALIDANDO','PROCESSANDO') AND lease_expira_em<@watchdog_agora
ORDER BY lease_expira_em;

-- Backlog e idade do lote PENDENTE mais antigo.
SELECT COUNT_BIG(*) lotes_pendentes,MIN(criado_em) lote_pendente_mais_antigo,
       DATEDIFF(MINUTE,MIN(criado_em),@watchdog_agora) idade_mais_antigo_minutos
FROM ingestao.lote
WHERE status='PENDENTE';

-- Modo de carga inicial: duração é observada, nunca encerrada automaticamente pelo watchdog.
SELECT ativo,ativado_em,desativado_em,alterado_por,observacao,atualizado_em,
       CASE WHEN ativo=1 AND ativado_em IS NOT NULL THEN DATEDIFF(MINUTE,ativado_em,@watchdog_agora) END ativo_ha_minutos
FROM controle.modo_carga_inicial
WHERE estado_id=1;
