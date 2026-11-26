-- Executed administratively by the disposable Actions harness. The
-- application name is assigned only to the DEV Runner/scorer SQL pool.
SET NOCOUNT ON;
IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'JornadaV9FullProbe')
    THROW 52992, 'V9_FULL_PROBE_SESSION_REUSE', 1;
GO
CREATE EVENT SESSION JornadaV9FullProbe ON SERVER
    ADD EVENT sqlserver.rpc_completed(
        WHERE ([sqlserver].[client_app_name] = N'JornadaV9Phase4Runner')),
    ADD EVENT sqlserver.sql_batch_completed(
        WHERE ([sqlserver].[client_app_name] = N'JornadaV9Phase4Runner'))
    ADD TARGET package0.event_file(
        SET filename=N'/var/opt/mssql/data/$(ProbeFile).xel',
            max_file_size=64, max_rollover_files=4)
    WITH (EVENT_RETENTION_MODE=NO_EVENT_LOSS, MAX_DISPATCH_LATENCY=1 SECONDS);
GO
CREATE EVENT SESSION JornadaV9FullStatements ON SERVER
    ADD EVENT sqlserver.sql_statement_completed(
        WHERE ([sqlserver].[client_app_name] = N'JornadaV9Phase4Runner')),
    ADD EVENT sqlserver.sp_statement_completed(
        WHERE ([sqlserver].[client_app_name] = N'JornadaV9Phase4Runner'))
    ADD TARGET package0.event_counter
    WITH (EVENT_RETENTION_MODE=NO_EVENT_LOSS);
GO
ALTER EVENT SESSION JornadaV9FullStatements ON SERVER STATE=START;
ALTER EVENT SESSION JornadaV9FullProbe ON SERVER STATE=START;
