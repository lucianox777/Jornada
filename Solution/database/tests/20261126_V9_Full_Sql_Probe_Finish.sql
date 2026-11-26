SET NOCOUNT ON;
DECLARE @statements xml, @requests xml;
SELECT @statements=TRY_CONVERT(xml, t.target_data)
FROM sys.dm_xe_sessions AS s
JOIN sys.dm_xe_session_targets AS t ON s.address=t.event_session_address
WHERE s.name=N'JornadaV9FullStatements' AND t.target_name=N'event_counter';

SELECT @requests=TRY_CONVERT(xml, t.target_data)
FROM sys.dm_xe_sessions AS s
JOIN sys.dm_xe_session_targets AS t ON s.address=t.event_session_address
WHERE s.name=N'JornadaV9FullProbe' AND t.target_name=N'event_counter';

IF @statements IS NULL OR @requests IS NULL
    THROW 52992, 'V9_FULL_PROBE_COUNTER_TARGET_MISSING', 2;

IF (SELECT COUNT_BIG(*) FROM sys.dm_xe_sessions WHERE name IN(N'JornadaV9FullProbe', N'JornadaV9FullStatements')
      AND dropped_event_count=0 AND dropped_buffer_count=0 AND largest_event_dropped_size=0) <> 2
    THROW 52992, 'V9_FULL_PROBE_OMITTED_EVENTS', 3;

ALTER EVENT SESSION JornadaV9FullStatements ON SERVER STATE=STOP;
ALTER EVENT SESSION JornadaV9FullProbe ON SERVER STATE=STOP;

DECLARE @sqlStatements bigint, @procedureStatements bigint, @serverRpc bigint, @serverBatch bigint;
SELECT @sqlStatements=COALESCE(SUM(CASE WHEN n.e.value('(@name)[1]', 'varchar(64)')='sql_statement_completed'
                               THEN n.e.value('(@count)[1]', 'bigint') ELSE 0 END),0),
       @procedureStatements=COALESCE(SUM(CASE WHEN n.e.value('(@name)[1]', 'varchar(64)')='sp_statement_completed'
                               THEN n.e.value('(@count)[1]', 'bigint') ELSE 0 END),0)
FROM @statements.nodes('/CounterTarget/Packages/Package/Event') AS n(e);

SELECT @serverRpc=COALESCE(SUM(CASE WHEN n.e.value('(@name)[1]', 'varchar(64)')='rpc_completed'
                           THEN n.e.value('(@count)[1]', 'bigint') ELSE 0 END),0),
       @serverBatch=COALESCE(SUM(CASE WHEN n.e.value('(@name)[1]', 'varchar(64)')='sql_batch_completed'
                             THEN n.e.value('(@count)[1]', 'bigint') ELSE 0 END),0)
FROM @requests.nodes('/CounterTarget/Packages/Package/Event') AS n(e);

DECLARE @rpc bigint, @batch bigint, @cpu bigint, @noCpu bigint, @files int;
SELECT @rpc=COUNT_BIG(CASE WHEN f.object_name='rpc_completed' THEN 1 END),
       @batch=COUNT_BIG(CASE WHEN f.object_name='sql_batch_completed' THEN 1 END),
       @cpu=SUM(v.cpu), @noCpu=COUNT_BIG(CASE WHEN v.cpu IS NULL THEN 1 END),
       @files=COUNT(DISTINCT f.file_name)
FROM sys.fn_xe_file_target_read_file(N'/var/opt/mssql/data/$(ProbeFile)*.xel', NULL, NULL, NULL) AS f
CROSS APPLY (VALUES(CONVERT(xml,f.event_data))) AS x(e)
CROSS APPLY (VALUES(x.e.value('(/event/data[@name="cpu_time"]/value/text())[1]', 'bigint'))) AS v(cpu);

IF @rpc+@batch<=0 OR @sqlStatements+@procedureStatements<=0
   OR @cpu IS NULL OR @noCpu<>0 OR @files NOT BETWEEN 1 AND 4
    THROW 52992, 'V9_FULL_PROBE_INVALID_COUNTERS', 4;

-- A full set of four files is valid only if no earlier file was lost to rollover.
IF @serverRpc <> @rpc OR @serverBatch <> @batch
    THROW 52992, 'V9_FULL_PROBE_ARCHIVED_EVENT_COUNT_MISMATCH', 5;

DROP EVENT SESSION JornadaV9FullStatements ON SERVER;
DROP EVENT SESSION JornadaV9FullProbe ON SERVER;

SELECT CONVERT(nvarchar(1024), (
    SELECT @rpc AS completedRpc, @batch AS completedBatches,
           @sqlStatements AS completedSqlStatements, @procedureStatements AS completedProcedureStatements,
           @cpu AS topLevelSqlCpuMicroseconds, @files AS eventFiles,
           @serverRpc AS serverCompletedRpc, @serverBatch AS serverCompletedBatches
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
));
