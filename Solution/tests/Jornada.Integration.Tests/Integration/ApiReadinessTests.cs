using Jornada.Api;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class ApiReadinessTests
{
    [Test]
    public async Task Readiness_accepts_only_current_schema_marker_and_essential_objects()
    {
        var connectionString = RequireIntegrationConnection();
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(AppContext.BaseDirectory, "database", "Jornada_Fase1.sql"));
        }

        var temp = Path.Combine(Path.GetTempPath(), "jornada-readiness-" + Guid.NewGuid().ToString("N"));
        var bronze = Path.Combine(temp, "bronze");
        var staging = Path.Combine(temp, "staging");
        Directory.CreateDirectory(bronze);
        Directory.CreateDirectory(staging);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Jornada"] = connectionString
                })
                .Build();
            var connections = new SqlConnectionFactory(configuration);

            var probe = new ApiReadinessProbe(
                new SqlSchemaReadinessProbe(connections),
                new ApiOperationalPaths(bronze, staging, true, true));

            var result = await probe.CheckAsync(CancellationToken.None);
            Assert.That(result.Ready, Is.True);
            Assert.That(result.Checks.Single(x => x.Name == "sql").Code, Is.Null);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION.");
        var csb = new SqlConnectionStringBuilder(connectionString!);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) && !db.Contains("dev", StringComparison.OrdinalIgnoreCase) && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Banco de integração deve conter Test, Dev ou Local.");
        return connectionString!;
    }
}
