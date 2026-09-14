using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class OperationalMonitorHeartbeatTests
{
    [Test]
    public async Task Heartbeat_marks_component_running_and_then_stopped_for_the_same_instance()
    {
        var connectionString = RequireIntegrationConnection();
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
            await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
            await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "migrations", "20260914_Operational_Monitor.sql"));
        }

        var node = "TEST-" + Guid.NewGuid().ToString("N")[..12];
        const string component = "HeartbeatIntegration";
        using var cts = new CancellationTokenSource();
        var reporter = new OperationalRuntimeHeartbeat(
            new OperationalSqlAdapter(connectionString),
            node,
            component,
            TimeSpan.FromMilliseconds(100));

        var loop = reporter.RunAsync(cts.Token);
        try
        {
            var running = await WaitForStatusAsync(connectionString, node, component, "RUNNING", TimeSpan.FromSeconds(8));
            Assert.That(running.Status, Is.EqualTo("RUNNING"));
            Assert.That(running.ProcessId, Is.GreaterThan(0));
            Assert.That(running.HeartbeatAt, Is.GreaterThanOrEqualTo(running.StartedAt));
            Assert.That(running.StoppedAt, Is.Null);

            cts.Cancel();
            await loop;

            var stopped = await WaitForStatusAsync(connectionString, node, component, "STOPPED", TimeSpan.FromSeconds(8));
            Assert.That(stopped.Status, Is.EqualTo("STOPPED"));
            Assert.That(stopped.StoppedAt, Is.Not.Null);
            Assert.That(stopped.StoppedAt, Is.GreaterThanOrEqualTo(stopped.StartedAt));
        }
        finally
        {
            cts.Cancel();
            try { await loop; } catch (OperationCanceledException) { }
            await DeleteAsync(connectionString, node, component);
        }
    }

    private static async Task<HeartbeatRow> WaitForStatusAsync(
        string connectionString,
        string node,
        string component,
        string expected,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await ReadAsync(connectionString, node, component);
            if (row is not null && string.Equals(row.Status, expected, StringComparison.Ordinal))
                return row;
            await Task.Delay(100);
        }

        var last = await ReadAsync(connectionString, node, component);
        Assert.Fail($"Heartbeat não atingiu estado {expected}. Último estado: {last?.Status ?? "ausente"}.");
        throw new InvalidOperationException("Unreachable.");
    }

    private static async Task<HeartbeatRow?> ReadAsync(string connectionString, string node, string component)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT status,process_id,iniciado_em,heartbeat_em,encerrado_em
              FROM controle.runtime_componente
             WHERE node_id=@node AND componente=@component;
            """, connection);
        command.Parameters.Add(new SqlParameter("@node", System.Data.SqlDbType.NVarChar, 64) { Value = node });
        command.Parameters.Add(new SqlParameter("@component", System.Data.SqlDbType.NVarChar, 80) { Value = component });
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new HeartbeatRow(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetDateTimeOffset(2),
            reader.GetDateTimeOffset(3),
            reader.IsDBNull(4) ? null : reader.GetDateTimeOffset(4));
    }

    private static async Task DeleteAsync(string connectionString, string node, string component)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "DELETE FROM controle.runtime_componente WHERE node_id=@node AND componente=@component;",
            connection);
        command.Parameters.Add(new SqlParameter("@node", System.Data.SqlDbType.NVarChar, 64) { Value = node });
        command.Parameters.Add(new SqlParameter("@component", System.Data.SqlDbType.NVarChar, 80) { Value = component });
        await command.ExecuteNonQueryAsync();
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION.");
        var csb = new SqlConnectionStringBuilder(connectionString!);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("dev", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Banco de integração deve conter Test, Dev ou Local.");
        return connectionString!;
    }

    private sealed record HeartbeatRow(
        string Status,
        int ProcessId,
        DateTimeOffset StartedAt,
        DateTimeOffset HeartbeatAt,
        DateTimeOffset? StoppedAt);
}
