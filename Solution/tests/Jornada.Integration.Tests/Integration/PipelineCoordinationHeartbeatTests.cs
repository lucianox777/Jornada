using Jornada.Pipeline.Coordination;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class PipelineCoordinationHeartbeatTests
{
    [Test]
    public async Task Healthy_exclusive_lease_survives_multiple_heartbeats()
    {
        var connectionString = RequireIntegrationConnection();
        var coordinator = new SqlPipelineCoordinator(
            connectionString,
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            exclusiveIntentTimeout: TimeSpan.FromSeconds(1));

        await using var lease = await coordinator.AcquireExclusiveJobAsync(
            "TEST_HEALTHY_HEARTBEAT",
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        await Task.Delay(TimeSpan.FromMilliseconds(650));

        Assert.Multiple(() =>
        {
            Assert.That(lease.IsLost, Is.False,
                "Um lease com sessão e applocks íntegros não pode ser invalidado pelo próprio heartbeat.");
            Assert.That(lease.LostToken.IsCancellationRequested, Is.False,
                "O token fail-closed só deve ser cancelado quando a sessão ou os locks forem realmente perdidos.");
        });
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");

        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("dev", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");

        return connectionString;
    }
}
