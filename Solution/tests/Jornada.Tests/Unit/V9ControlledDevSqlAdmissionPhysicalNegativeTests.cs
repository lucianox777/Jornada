using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Unit;

/// <summary>
/// Opt-in physical SQL negative of the controlled DEV attempt binding.
/// No fixture/model, schema setup, DML, Gold publish or TEST data is used.
/// The dedicated DEV attempt database must exist externally.
/// </summary>
[TestFixture, Category("SQLSmoke")]
public sealed class V9ControlledDevSqlAdmissionPhysicalNegativeTests
{
    [Test]
    public async Task Disposable_DEV_database_refuses_another_attempt_before_any_run_write()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_V9_DEV_NEGATIVE_SQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Ignore("Somente SQL DEV descartável explícito; nenhuma conexão presumida.");
            return;
        }

        var database = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        const string prefix = "JornadaV9Dev_";
        if (!database.StartsWith(prefix, StringComparison.Ordinal)
            || database.Length != prefix.Length + 32
            || !Guid.TryParseExact(database[prefix.Length..], "N", out var databaseAttempt)
            || databaseAttempt == Guid.Empty)
            throw new InvalidOperationException(
                "SQLSmoke exige Initial Catalog=JornadaV9Dev_<GUID N> descartável.");

        // Use the real physical SQL engine but only issue a read-only probe.
        // An attempt different from the database name must fail closed,
        // regardless of whether the DB has matching owner markers.
        var differentAttempt = Guid.NewGuid();
        while (differentAttempt == databaseAttempt)
            differentAttempt = Guid.NewGuid();

        await using var sql = new SqlConnection(connectionString);
        await sql.OpenAsync();
        var error = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
            await V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(
                sql, differentAttempt, CancellationToken.None)));
        Assert.That(error!.Message,
            Is.EqualTo("RUNNER_V9_DEV_DISPOSABLE_SQL_NOT_VERIFIED"));

        await using var check = new SqlCommand("SELECT DB_NAME();", sql);
        Assert.That((string?)await check.ExecuteScalarAsync(), Is.EqualTo(database));
    }
}
