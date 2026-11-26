SET NOCOUNT ON;
DECLARE @events xml;
SELECT @events=TRY_CONVERT(xml, t.target_data)
FROM sys.dm_xe_sessions AS s
JOIN sys.dm_xe_session_targets AS t ON s.address=t.event_session_address
WHERE s.name=N'JornadaV9FullStatements' AND t.target_name=N'event_counter';

IF @events IS NULL OR
   (SELECT COUNT_BIG(*) FROM sys.dm_xe_sessions WHERE name IN(N'JornadaV9FullProbe', N'JornadaV9FullStatements')
      AND dropped_event_count=0 AND dropped_buffer_count=0 AND largest_event_dropped_size=0) <> 2
    THROW 52992, 'V9_FULL_PROBE_INCOMPLETE', 1;

ALTER EVENT SESSION JornadaV9FullStatements ON SERVER STATE=STOP;
ALTER EVENT SESSION JornadaV9FullProbe ON SERVER STATE=STOP;

DECLARE @sqlStatements bigint, @procedureStatements bigint;
SELECT @sqlStatements=COALESCE(SUM(CASE WHEN n.e.value('(@name)[1]', 'varchar(64)')='sql_statement_completed'
                               THEN n.e.value('(@count)[1]', 'bigint') ELSE 0 END),0),
       @procedureStatements=COALESCE(SUM(CASE WHEN n.e.value('(@name)[1]', 'varchar(64)')='sp_statement_completed'
                               THEN n.e.value('(@count)[1]', 'bigint') ELSE 0 END),0)
FROM @events.nodes('/CounterTarget/Packages/Package/Event') AS n(e);

DECLARE @rpc bigint, @batch bigint, @cpu bigint, @noCpu bigint, @files int;
SELECT @rpc=COUNT_BIG(CASE WHEN f.object_name='rpc_completed' THEN 1 END),
       @batch=COUNT_BIG(CASE WHEN f.object_name='sql_batch_completed' THEN 1 END),
       @cpu=SUM(v.cpu), @noCpu=COUNT_BIG(CASE WHEN v.cpu IS NULL THEN 1 END),
       @files=COUNT(DISTINCT f.file_name)
FROM sys.fn_xe_file_target_read_file(N'/var/opt/mssql/data/$(ProbeFile)*.xel', NULL, NULL, NULL) AS f
CROSS APPLY (VALUES(CONVERT(xml,f.event_data))) AS x(e)
CROSS APPLY (VALUES(x.e.value('(/event/data[@name="cpu_time"]/value/text())[1]', 'bigint'))) AS v(cpu);

IF @rpc+@batch<=0 OR @sqlStatements+@procedureStatements<=0
   OR @cpu IS NULL OR @noCpu<>0 OR @files NOT BETWEEN 1 AND 3
    THROW 52992, 'V9_FULL_PROBE_INVALID_COUNTERS', 1;

DROP EVENT SESSION JornadaV9FullStatements ON SERVER;
DROP EVENT SESSION JornadaV9FullProbe ON SERVER;

SELECT @rpc AS completedRpc, @batch AS completedBatches,
       @sqlStatements AS completedSqlStatements, @procedureStatements AS completedProcedureStatements,
       @cpu AS topLevelSqlCpuMicroseconds, @files AS eventFiles
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
