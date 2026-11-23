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
    [Test]
    public async Task DEV_database_with_wrong_resident_properties_fails_before_run_write()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "JORNADA_V9_DEV_WRONG_PROPERTY_SQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Ignore("Somente banco DEV descartável explicitamente marcado para negativa de propriedades.");
            return;
        }

        var database = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        const string prefix = "JornadaV9Dev_";
        if (!database.StartsWith(prefix, StringComparison.Ordinal)
            || database.Length != prefix.Length + 32
            || !Guid.TryParseExact(database[prefix.Length..], "N", out var attempt)
            || attempt == Guid.Empty)
            throw new InvalidOperationException(
                "Negativa de propriedades requer banco JornadaV9Dev_<GUID N> descartável.");

        await using var sql = new SqlConnection(connectionString);
        await sql.OpenAsync();
        // Require the NEGATIVE fixture to be genuinely misconfigured; an
        // accidentally healthy database must never satisfy this assertion.
        await using var markers = new SqlCommand("""
            SELECT CASE WHEN EXISTS(
                SELECT 1 FROM sys.extended_properties
                 WHERE class=0 AND name=N'Jornada.EnvironmentProfile'
                   AND CONVERT(nvarchar(200), value)=N'Development')
              AND EXISTS(
                SELECT 1 FROM sys.extended_properties
                 WHERE class=0 AND name=N'Jornada.V9.DevAttempt'
                   AND CONVERT(nvarchar(200),value)=@attempt)
            THEN 1 ELSE 0 END;
            """, sql);
        markers.Parameters.Add("@attempt", System.Data.SqlDbType.NVarChar, 36).Value =
            attempt.ToString("D");
        var hasCorrectMarkers = Convert.ToInt32(await markers.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture) == 1;
        if (hasCorrectMarkers)
            throw new InvalidOperationException(
                "Fixture negativa inválida: propriedades residentes já são corretas.");

        var error = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
            V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(
                sql, attempt, CancellationToken.None)));
        Assert.That(error!.Message,
            Is.EqualTo("RUNNER_V9_DEV_DISPOSABLE_SQL_NOT_VERIFIED"));
    }

    [Test]
    public async Task DEV_database_rejects_nonexistent_run_before_candidate_or_Gold_reads()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "JORNADA_V9_DEV_VALID_ATTEMPT_SQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Ignore("Exige tentativa DEV descartável com propriedades residentes válidas.");
            return;
        }

        var database = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        const string prefix = "JornadaV9Dev_";
        if (!database.StartsWith(prefix, StringComparison.Ordinal)
            || database.Length != prefix.Length + 32
            || !Guid.TryParseExact(database[prefix.Length..], "N", out var attempt)
            || attempt == Guid.Empty)
            throw new InvalidOperationException("SQL DEV deve usar o nome JornadaV9Dev_<GUID N>.");

        await using var sql = new SqlConnection(connectionString);
        await sql.OpenAsync();
        // A valid physical DEV attempt is a fixture prerequisite here;
        // do not confuse a wrong database with proof of a nonexistent run.
        await V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(
            sql, attempt, CancellationToken.None);

        var missingRun = Guid.NewGuid();
        await using (var check = new SqlCommand(
            "SELECT COUNT_BIG(*) FROM identidade.linkage_run WHERE linkage_run_id=@run;", sql))
        {
            var param = check.Parameters.Add("@run", System.Data.SqlDbType.UniqueIdentifier);
            while (true)
            {
                param.Value = missingRun;
                if (Convert.ToInt64(await check.ExecuteScalarAsync(),
                    System.Globalization.CultureInfo.InvariantCulture) == 0)
                    break;
                missingRun = Guid.NewGuid();
            }
        }

        var winner = V9OperationalPinnedCohortRuntime.FromVerifiedFileSnapshot(
            V9FittedSnapshotTests.CompleteTf4OperationalWinner());
        var error = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(() =>
            V9ControlledDevOperationalCandidateSource.ReadAsync(
                sql, winner, attempt, missingRun,
                Array.Empty<V9FrozenRunObservation>(), 10, CancellationToken.None)));
        Assert.That(error!.Message, Is.EqualTo("RUNNER_V9_DEV_RUN_HEADER_MISMATCH"));
    }

}
