using Jornada.Operational.Sql;
using Jornada.Api;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class ApiReadinessTests
{
    [Test]
    public async Task Readiness_rejects_legacy_schema_369_even_when_legacy_objects_exist()
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
            var connections = new OperationalSqlAdapter(connectionString!);

            var probe = new ApiReadinessProbe(
                new SqlSchemaReadinessProbe(connections),
                new ApiOperationalPaths(bronze, staging, true, true));

            var result = await probe.CheckAsync(CancellationToken.None);
            Assert.That(result.Ready, Is.False);
            var sql = result.Checks.Single(x => x.Name == "sql");
            Assert.That(sql.Ready, Is.False);
            Assert.That(sql.Code, Is.EqualTo("SQL_SCHEMA_INCOMPATIVEL"));
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
