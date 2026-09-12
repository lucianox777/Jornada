using Jornada.Operational.Sql;
using Jornada.Api;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class ApiReadinessTests
{
    [Test]
    public async Task Readiness_accepts_only_current_schema_marker_and_essential_objects_rejecting_legacy_369()
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

    [Test]
    public async Task Readiness_is_fail_closed_outside_development_while_corporate_identity_and_authorization_are_pending()
    {
        var temp = Path.Combine(Path.GetTempPath(), "jornada-security-readiness-" + Guid.NewGuid().ToString("N"));
        var bronze = Path.Combine(temp, "bronze");
        var staging = Path.Combine(temp, "staging");
        Directory.CreateDirectory(bronze);
        Directory.CreateDirectory(staging);

        var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestHostEnvironment("Production"))
            .BuildServiceProvider();

        try
        {
            var probe = new ApiReadinessProbe(
                new AlwaysReadySqlProbe(),
                new ApiOperationalPaths(bronze, staging, true, true),
                services);

            var result = await probe.CheckAsync(CancellationToken.None);

            Assert.That(result.Ready, Is.False);
            var security = result.Checks.Single(x => x.Name == "corporate-identity");
            Assert.That(security.Ready, Is.False);
            Assert.That(security.Code, Is.EqualTo("CORPORATE_IDENTITY_AUTHORIZATION_PENDING"));
            Assert.That(result.Checks.Single(x => x.Name == "sql").Ready, Is.True);
            Assert.That(result.Checks.Single(x => x.Name == "bronze").Ready, Is.True);
            Assert.That(result.Checks.Single(x => x.Name == "staging").Ready, Is.True);
        }
        finally
        {
            services.Dispose();
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

    private sealed class AlwaysReadySqlProbe : ISqlReadinessProbe
    {
        public Task<ApiReadinessCheck> CheckAsync(CancellationToken ct) =>
            Task.FromResult(new ApiReadinessCheck("sql", true));
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Jornada.Tests.Integration";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
