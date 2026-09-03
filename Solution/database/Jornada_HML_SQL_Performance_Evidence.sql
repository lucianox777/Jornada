SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Coleta somente métricas técnicas agregadas. Não retorna texto SQL, parâmetros, identificadores de pessoa nem payloads.
DECLARE @query_store_state NVARCHAR(60) = (
    SELECT TOP (1) actual_state_desc FROM sys.database_query_store_options
);
DECLARE @blocked_requests BIGINT = (
    SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE session_id <> @@SPID AND blocking_session_id <> 0
);
DECLARE @lock_wait_tasks BIGINT = (
    SELECT COALESCE(SUM(CONVERT(BIGINT, waiting_tasks_count)),0) FROM sys.dm_os_wait_stats WHERE wait_type LIKE N'LCK_M[_]%'
);
DECLARE @lock_wait_ms BIGINT = (
    SELECT COALESCE(SUM(CONVERT(BIGINT, wait_time_ms)),0) FROM sys.dm_os_wait_stats WHERE wait_type LIKE N'LCK_M[_]%'
);
DECLARE @deadlocks_raw BIGINT = (
    SELECT COALESCE(MAX(CONVERT(BIGINT, cntr_value)),0)
    FROM sys.dm_os_performance_counters
    WHERE counter_name = N'Number of Deadlocks/sec' AND instance_name = N'_Total'
);

SELECT (
    SELECT
      1 AS schemaVersion,
      N'PASS' AS collectionStatus,
      DB_NAME() AS databaseName,
      CONVERT(NVARCHAR(33), SYSDATETIMEOFFSET(), 127) AS capturedAt,
      @query_store_state AS queryStoreState,
      @blocked_requests AS blockedRequests,
      @lock_wait_tasks AS cumulativeLockWaitTasks,
      @lock_wait_ms AS cumulativeLockWaitMilliseconds,
      @deadlocks_raw AS cumulativeDeadlocksRaw
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
) AS evidence_json;
