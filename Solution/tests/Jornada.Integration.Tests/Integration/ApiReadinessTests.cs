using System.Security.Cryptography;
using Jornada.Operational.Sql;
using Jornada.Api;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class ApiReadinessTests
{
    [Test]
    public async Task Readiness_rejects_baseline_only_and_accepts_complete_operational_manifest()
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

            var baselineOnly = await probe.CheckAsync(CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(baselineOnly.Ready, Is.False);
                Assert.That(baselineOnly.Checks.Single(x => x.Name == "sql").Code, Is.EqualTo("SQL_SCHEMA_INCOMPATIVEL"));
            });

            await ApplyOperationalManifestAsync(connectionString!);

            var complete = await probe.CheckAsync(CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(complete.Ready, Is.True);
                Assert.That(complete.Checks.Single(x => x.Name == "sql").Code, Is.Null);
            });
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    private static async Task ApplyOperationalManifestAsync(string connectionString)
    {
        var databaseRoot = Path.Combine(AppContext.BaseDirectory, "database");
        var migrationsRoot = Path.Combine(databaseRoot, "migrations");
        var manifest = Path.Combine(migrationsRoot, "manifest.txt");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseRoot, "Jornada_Seed_Dev.sql"));

        await using (var history = connection.CreateCommand())
        {
            history.CommandText = """
                IF SCHEMA_ID(N'jornada') IS NULL EXEC(N'CREATE SCHEMA jornada');
                IF OBJECT_ID(N'jornada.schema_migration',N'U') IS NULL
                CREATE TABLE jornada.schema_migration(
                    migration_name NVARCHAR(260) NOT NULL PRIMARY KEY,
                    sha256 CHAR(64) NOT NULL,
                    applied_at DATETIME2(3) NOT NULL CONSTRAINT DF_jornada_schema_migration_test_applied_at DEFAULT SYSUTCDATETIME());
                """;
            await history.ExecuteNonQueryAsync();
        }

        foreach (var raw in await File.ReadAllLinesAsync(manifest))
        {
            var entry = raw.Split('#', 2)[0].Trim();
            if (entry.Length == 0) continue;
            var migrationPath = Path.Combine(migrationsRoot, entry);
            await SqlBatchRunner.ExecuteFileAsync(connection, migrationPath);

            var checksum = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(migrationPath))).ToLowerInvariant();
            await using var record = connection.CreateCommand();
            record.CommandText = """
                IF NOT EXISTS(SELECT 1 FROM jornada.schema_migration WHERE migration_name=@name)
                    INSERT INTO jornada.schema_migration(migration_name,sha256) VALUES(@name,@sha256);
                ELSE IF EXISTS(SELECT 1 FROM jornada.schema_migration WHERE migration_name=@name AND sha256<>@sha256)
                    THROW 51000,'Checksum de migração divergente no teste de readiness.',1;
                """;
            record.Parameters.AddWithValue("@name", entry);
            record.Parameters.AddWithValue("@sha256", checksum);
            await record.ExecuteNonQueryAsync();
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
