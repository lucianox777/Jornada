using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Jornada.Operational.Sql;

/// <summary>
/// Publica presença viva de um processo residente no SQL compartilhado do cluster.
/// Falhas de monitoramento são best-effort e nunca derrubam o workload monitorado.
/// </summary>
public sealed class OperationalRuntimeHeartbeat(
    IOperationalSqlAdapter connections,
    string nodeId,
    string component,
    TimeSpan? interval = null)
{
    private readonly string nodeId = Normalize(nodeId, 64, nameof(nodeId));
    private readonly string component = Normalize(component, 80, nameof(component));
    private readonly TimeSpan interval = interval is { } configured && configured > TimeSpan.Zero
        ? configured
        : TimeSpan.FromSeconds(10);
    private readonly Guid instanceId = Guid.NewGuid();
    private readonly string machineName = Normalize(Environment.MachineName, 128, nameof(Environment.MachineName));
    private readonly int processId = Environment.ProcessId;
    private readonly string? version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        await PulseBestEffortAsync(stoppingToken);
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await PulseBestEffortAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await MarkStoppedBestEffortAsync();
        }
    }

    private async Task PulseBestEffortAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new SqlCommand("""
                DECLARE @agora DATETIMEOFFSET(7)=SYSUTCDATETIME();
                MERGE controle.runtime_componente WITH (HOLDLOCK) AS alvo
                USING (SELECT @node_id AS node_id,@componente AS componente) AS origem
                  ON alvo.node_id=origem.node_id AND alvo.componente=origem.componente
                WHEN MATCHED THEN UPDATE SET
                    machine_name=@machine_name,
                    instance_id=@instance_id,
                    process_id=@process_id,
                    status=N'RUNNING',
                    iniciado_em=CASE WHEN alvo.instance_id=@instance_id THEN alvo.iniciado_em ELSE @agora END,
                    heartbeat_em=@agora,
                    encerrado_em=NULL,
                    versao=@versao
                WHEN NOT MATCHED THEN INSERT(
                    node_id,componente,machine_name,instance_id,process_id,status,iniciado_em,heartbeat_em,encerrado_em,versao)
                VALUES(
                    @node_id,@componente,@machine_name,@instance_id,@process_id,N'RUNNING',@agora,@agora,NULL,@versao);
                """, connection)
            {
                CommandTimeout = 5
            };
            Add(command, "@node_id", nodeId, 64);
            Add(command, "@componente", component, 80);
            Add(command, "@machine_name", machineName, 128);
            command.Parameters.Add(new SqlParameter("@instance_id", System.Data.SqlDbType.UniqueIdentifier) { Value = instanceId });
            command.Parameters.Add(new SqlParameter("@process_id", System.Data.SqlDbType.Int) { Value = processId });
            Add(command, "@versao", (object?)version ?? DBNull.Value, 80);
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Heartbeat operacional indisponível; componente={0}; node={1}; tipo={2}",
                component,
                nodeId,
                ex.GetType().Name);
        }
    }

    private async Task MarkStoppedBestEffortAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await using var connection = await connections.OpenAsync(timeout.Token);
            await using var command = new SqlCommand("""
                UPDATE controle.runtime_componente
                   SET status=N'STOPPED',heartbeat_em=SYSUTCDATETIME(),encerrado_em=SYSUTCDATETIME()
                 WHERE node_id=@node_id AND componente=@componente AND instance_id=@instance_id;
                """, connection)
            {
                CommandTimeout = 3
            };
            Add(command, "@node_id", nodeId, 64);
            Add(command, "@componente", component, 80);
            command.Parameters.Add(new SqlParameter("@instance_id", System.Data.SqlDbType.UniqueIdentifier) { Value = instanceId });
            await command.ExecuteNonQueryAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or OperationCanceledException or TimeoutException)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Falha best-effort ao encerrar heartbeat; componente={0}; node={1}; tipo={2}",
                component,
                nodeId,
                ex.GetType().Name);
        }
    }

    private static void Add(SqlCommand command, string name, object value, int size)
    {
        command.Parameters.Add(new SqlParameter(name, System.Data.SqlDbType.NVarChar, size) { Value = value });
    }

    private static string Normalize(string value, int maxLength, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Valor obrigatório.", parameterName);
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
