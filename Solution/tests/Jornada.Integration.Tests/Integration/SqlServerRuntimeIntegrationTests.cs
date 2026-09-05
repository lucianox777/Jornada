using System.Data;
using Microsoft.Data.SqlClient;
using NUnit.Framework;
using Jornada.Tests.Integration.Infrastructure;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class SqlServerRuntimeIntegrationTests
{
    [Test]
    public async Task ConfiguredSqlTargetIsReachableAndEngineContractIsExplicitAsync()
    {
        await using var connection = new SqlConnection(SqlIntegrationEnvironment.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS nvarchar(32));";

        var majorText = Convert.ToString(
            await command.ExecuteScalarAsync().ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.That(majorText, Is.Not.Null.And.Not.Empty,
            "O endpoint Microsoft SQL deve expor ProductMajorVersion.");

        if (string.Equals(
                SqlIntegrationEnvironment.Target,
                SqlIntegrationEnvironment.SqlServer2022Target,
                StringComparison.Ordinal))
        {
            Assert.That(
                int.Parse(majorText!, System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo(16),
                "A baseline local/CI continua contratada para SQL Server 2022.");
        }
        else
        {
            Assert.That(SqlIntegrationEnvironment.IsFabricSqlDatabase, Is.True);
            Assert.That(SqlIntegrationEnvironment.UseExistingExternalDatabase, Is.True,
                "Fabric deve usar banco de compatibilidade pré-provisionado e isolado.");

            var builder = new SqlConnectionStringBuilder(SqlIntegrationEnvironment.ConnectionString);
            Assert.That(builder.InitialCatalog, Is.Not.Null.And.Not.Empty,
                "O alvo Fabric deve declarar explicitamente o banco de compatibilidade.");
        }
    }

    [Test]
    public void IntegrationConnectionIsIsolatedAndPoolingIsDisabled()
    {
        var builder = new SqlConnectionStringBuilder(SqlIntegrationEnvironment.ConnectionString);

        Assert.That(builder.Pooling, Is.False,
            "A suíte Integration não deve reutilizar sessões físicas por pooling.");

        if (!SqlIntegrationEnvironment.UseExistingExternalDatabase)
        {
            Assert.That(builder.InitialCatalog, Does.StartWith("JornadaIntegration_"),
                "O banco deve ser exclusivo por execução quando não há opt-out explícito.");
        }
        else
        {
            Assert.That(
                builder.InitialCatalog.Contains("test", StringComparison.OrdinalIgnoreCase)
                || builder.InitialCatalog.Contains("dev", StringComparison.OrdinalIgnoreCase)
                || builder.InitialCatalog.Contains("local", StringComparison.OrdinalIgnoreCase),
                Is.True,
                "Banco externo de Integration deve ser explicitamente não produtivo.");
        }
    }

    [Test]
    public async Task TransactionRollbackIsEnforcedBySqlServerAsync()
    {
        await using var connection = new SqlConnection(SqlIntegrationEnvironment.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            "CREATE TABLE #RollbackProbe (Id int NOT NULL PRIMARY KEY);",
            transaction: null).ConfigureAwait(false);

        await using (var transaction = connection.BeginTransaction())
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO #RollbackProbe (Id) VALUES (1);",
                transaction).ConfigureAwait(false);

            await transaction.RollbackAsync().ConfigureAwait(false);
        }

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT_BIG(*) FROM #RollbackProbe;";
        var count = Convert.ToInt64(
            await countCommand.ExecuteScalarAsync().ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.That(count, Is.Zero);
    }

    [Test]
    public async Task CheckConstraintIsEnforcedBySqlServerAsync()
    {
        await using var connection = new SqlConnection(SqlIntegrationEnvironment.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            "CREATE TABLE #ConstraintProbe (Value int NOT NULL CHECK (Value > 0));",
            transaction: null).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO #ConstraintProbe (Value) VALUES (-1);";

        var exception = Assert.ThrowsAsync<SqlException>(async () =>
        {
            _ = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        });

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Number, Is.EqualTo(547));
    }

    [Test]
    public async Task IndependentConnectionsObserveRealLocksAsync()
    {
        var tableName = $"##JornadaLockProbe_{Guid.NewGuid():N}";

        await using var first = new SqlConnection(SqlIntegrationEnvironment.ConnectionString);
        await using var second = new SqlConnection(SqlIntegrationEnvironment.ConnectionString);
        await first.OpenAsync().ConfigureAwait(false);
        await second.OpenAsync().ConfigureAwait(false);

        try
        {
            await ExecuteAsync(
                first,
                $"CREATE TABLE {tableName} (Id int NOT NULL PRIMARY KEY, Value int NOT NULL); " +
                $"INSERT INTO {tableName} (Id, Value) VALUES (1, 1);",
                transaction: null).ConfigureAwait(false);

            await using var transaction = first.BeginTransaction(IsolationLevel.ReadCommitted);

            await ExecuteAsync(
                first,
                $"UPDATE {tableName} SET Value = 2 WHERE Id = 1;",
                transaction).ConfigureAwait(false);

            await using var blockedUpdate = second.CreateCommand();
            blockedUpdate.CommandText =
                $"SET LOCK_TIMEOUT 1000; UPDATE {tableName} SET Value = 3 WHERE Id = 1;";
            blockedUpdate.CommandTimeout = 5;

            var exception = Assert.ThrowsAsync<SqlException>(async () =>
            {
                _ = await blockedUpdate.ExecuteNonQueryAsync().ConfigureAwait(false);
            });

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Number, Is.EqualTo(1222),
                "A segunda conexão deveria expirar esperando o lock exclusivo da primeira.");

            await transaction.RollbackAsync().ConfigureAwait(false);

            await using var read = second.CreateCommand();
            read.CommandText = $"SELECT Value FROM {tableName} WHERE Id = 1;";
            var value = Convert.ToInt32(
                await read.ExecuteScalarAsync().ConfigureAwait(false),
                System.Globalization.CultureInfo.InvariantCulture);

            Assert.That(value, Is.EqualTo(1), "Rollback deve restaurar o estado anterior ao lock.");
        }
        finally
        {
            await using var cleanup = first.CreateCommand();
            cleanup.CommandText = $"DROP TABLE IF EXISTS {tableName};";
            await cleanup.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static async Task ExecuteAsync(
        SqlConnection connection,
        string sql,
        SqlTransaction? transaction)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.CommandTimeout = 15;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
