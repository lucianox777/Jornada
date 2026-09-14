using Jornada.Api;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class OperationalMonitorConfigurationHealthTests
{
    [Test]
    public async Task Configuration_health_is_ok_only_when_api_components_and_database_share_the_expected_identity()
    {
        var connectionString = RequireIntegrationConnection();
        string? priorDatabaseSchema;
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
            await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
            await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "migrations", "20260914_Operational_Monitor.sql"));
            priorDatabaseSchema = await ReadSolutionSchemaAsync(connection);
            await SetSolutionSchemaAsync(connection, "3.70");
        }

        var priorBundle = Environment.GetEnvironmentVariable("JORNADA_CONFIGURATION_BUNDLE_VERSION");
        var priorSchema = Environment.GetEnvironmentVariable("JORNADA_SOLUTION_SCHEMA_VERSION");
        Environment.SetEnvironmentVariable("JORNADA_CONFIGURATION_BUNDLE_VERSION", "3.70-config.test");
        Environment.SetEnvironmentVariable("JORNADA_SOLUTION_SCHEMA_VERSION", "3.70");

        var node = "CFG-" + Guid.NewGuid().ToString("N")[..12];
        try
        {
            await UpsertComponentAsync(connectionString, node, "Api", "3.70-config.test", "3.70");
            var monitor = new OperationalMonitorService(new OperationalSqlAdapter(connectionString));

            var healthy = await monitor.GetAsync(CancellationToken.None);
            Assert.That(healthy.ConfigurationHealth.Status, Is.EqualTo("OK"));
            Assert.That(healthy.ConfigurationHealth.ExpectedBundleVersion, Is.EqualTo("3.70-config.test"));
            Assert.That(healthy.ConfigurationHealth.DatabaseSolutionSchema, Is.EqualTo("3.70"));

            await UpsertComponentAsync(connectionString, node, "Api", "3.70-config.old", "3.70");
            var divergentNode = await monitor.GetAsync(CancellationToken.None);
            Assert.That(divergentNode.ConfigurationHealth.Status, Is.EqualTo("DIVERGENTE"));
            Assert.That(divergentNode.OverallStatus, Is.EqualTo("FALHA"));

            await UpsertComponentAsync(connectionString, node, "Api", "3.70-config.test", "3.70");
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await SetSolutionSchemaAsync(connection, "3.69");
            }
            var divergentDatabase = await monitor.GetAsync(CancellationToken.None);
            Assert.That(divergentDatabase.ConfigurationHealth.Status, Is.EqualTo("DIVERGENTE"));
            Assert.That(divergentDatabase.ConfigurationHealth.DatabaseSolutionSchema, Is.EqualTo("3.69"));
            Assert.That(divergentDatabase.OverallStatus, Is.EqualTo("FALHA"));
        }
        finally
        {
            await DeleteNodeAsync(connectionString, node);
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await RestoreSolutionSchemaAsync(connection, priorDatabaseSchema);
            Environment.SetEnvironmentVariable("JORNADA_CONFIGURATION_BUNDLE_VERSION", priorBundle);
            Environment.SetEnvironmentVariable("JORNADA_SOLUTION_SCHEMA_VERSION", priorSchema);
        }
    }

    private static async Task UpsertComponentAsync(
        string connectionString,
        string node,
        string component,
        string bundle,
        string expectedSchema)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            MERGE controle.runtime_componente WITH (HOLDLOCK) AS target
            USING (SELECT @node AS node_id,@component AS componente) AS source
              ON target.node_id=source.node_id AND target.componente=source.componente
            WHEN MATCHED THEN UPDATE SET
              machine_name=N'integration',instance_id=@instance,process_id=1,status=N'RUNNING',
              iniciado_em=SYSUTCDATETIME(),heartbeat_em=SYSUTCDATETIME(),encerrado_em=NULL,
              versao=N'test',configuration_bundle_version=@bundle,solution_schema_expected=@schema
            WHEN NOT MATCHED THEN INSERT(
              node_id,componente,machine_name,instance_id,process_id,status,iniciado_em,heartbeat_em,encerrado_em,versao,
              configuration_bundle_version,solution_schema_expected)
            VALUES(@node,@component,N'integration',@instance,1,N'RUNNING',SYSUTCDATETIME(),SYSUTCDATETIME(),NULL,N'test',@bundle,@schema);
            """, connection);
        command.Parameters.Add(new SqlParameter("@node", System.Data.SqlDbType.NVarChar, 64) { Value = node });
        command.Parameters.Add(new SqlParameter("@component", System.Data.SqlDbType.NVarChar, 80) { Value = component });
        command.Parameters.Add(new SqlParameter("@instance", System.Data.SqlDbType.UniqueIdentifier) { Value = Guid.NewGuid() });
        command.Parameters.Add(new SqlParameter("@bundle", System.Data.SqlDbType.NVarChar, 80) { Value = bundle });
        command.Parameters.Add(new SqlParameter("@schema", System.Data.SqlDbType.NVarChar, 32) { Value = expectedSchema });
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteNodeAsync(string connectionString, string node)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "DELETE FROM controle.runtime_componente WHERE node_id=@node;",
            connection);
        command.Parameters.Add(new SqlParameter("@node", System.Data.SqlDbType.NVarChar, 64) { Value = node });
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ReadSolutionSchemaAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand("""
            SELECT CONVERT(NVARCHAR(32),(
              SELECT value FROM sys.extended_properties
              WHERE class=0 AND name=N'Jornada.SolutionSchema'));
            """, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task SetSolutionSchemaAsync(SqlConnection connection, string value)
    {
        await using var command = new SqlCommand("""
            IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema')
              EXEC sys.sp_updateextendedproperty @name=N'Jornada.SolutionSchema',@value=@value;
            ELSE
              EXEC sys.sp_addextendedproperty @name=N'Jornada.SolutionSchema',@value=@value;
            """, connection);
        command.Parameters.Add(new SqlParameter("@value", System.Data.SqlDbType.NVarChar, 32) { Value = value });
        await command.ExecuteNonQueryAsync();
    }

    private static async Task RestoreSolutionSchemaAsync(SqlConnection connection, string? priorValue)
    {
        if (priorValue is not null)
        {
            await SetSolutionSchemaAsync(connection, priorValue);
            return;
        }

        await using var command = new SqlCommand("""
            IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema')
              EXEC sys.sp_dropextendedproperty @name=N'Jornada.SolutionSchema';
            """, connection);
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
}
