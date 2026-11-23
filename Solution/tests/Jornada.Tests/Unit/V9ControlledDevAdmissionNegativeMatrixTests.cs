using Jornada.Linkage.Runner;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Jornada.Tests.Unit;

/// <summary>
/// Narrow DEV-negative admission matrix derived from PR #1767's UNAPPROVED
/// TEST_ONLY proposal. This does not import TEST admission, the harness name,
/// fixture caps or nonpublication into controlled DEV. It proves rejection
/// before SQL for missing attempt and absent/legacy winner selection.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class V9ControlledDevAdmissionNegativeMatrixTests
{
    private sealed class NeverOpenSql : IOperationalSqlAdapter
    {
        public int ConnectionRequests { get; private set; }
        private InvalidOperationException Block()
        {
            ConnectionRequests++;
            return new("NEGATIVE_MATRIX_SQL_MUST_NOT_BE_OPENED");
        }

        public SqlConnection CreateConnection() => throw Block();
        public SqlConnection CreateDedicatedSessionConnection() => throw Block();
        public Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
            => throw Block();
        public Task<SqlConnection> OpenDedicatedSessionAsync(CancellationToken cancellationToken = default)
            => throw Block();
    }

    private static IConfiguration Selection(int? version, string? file) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Linkage:SelectedModelVersion"] = version?.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ["Linkage:V9SelectionFile"] = file
            }).Build();

    [Test]
    public void Missing_attempt_fails_before_loading_winner_or_connecting_to_SQL()
    {
        var sql = new NeverOpenSql();
        var error = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
            await V9ControlledDevSelection.VerifyAsync(
                Selection(9, "never-load-a-selection.json"), sql,
                Guid.Empty, CancellationToken.None)));
        Assert.That(error!.Message, Is.EqualTo("RUNNER_V9_DEV_ATTEMPT_MISSING"));
        Assert.That(sql.ConnectionRequests, Is.Zero);
    }

    [Test]
    public void Physical_preflight_rejects_unbound_attempt_or_closed_connection_without_SQL()
    {
        using var closed = new SqlConnection();
        var empty = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
            V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(
                closed, Guid.Empty, CancellationToken.None)));
        Assert.That(empty!.Message, Is.EqualTo("RUNNER_V9_DEV_ATTEMPT_MISSING"));

        var inaccessible = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
            V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(
                closed, Guid.NewGuid(), CancellationToken.None)));
        Assert.That(inaccessible!.Message,
            Is.EqualTo("RUNNER_V9_DEV_DISPOSABLE_SQL_NOT_VERIFIED"));
        Assert.That(closed.State, Is.EqualTo(System.Data.ConnectionState.Closed));
    }

    [TestCase(null, null)]
    [TestCase(1, "legacy-model.json")]
    [TestCase(9, null)]
    [TestCase(9, "")]
    [TestCase(9, " ")]
    public void Missing_or_legacy_selection_fails_before_SQL(
        int? modelVersion, string? file)
    {
        var sql = new NeverOpenSql();
        var error = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
            await V9ControlledDevSelection.VerifyAsync(
                Selection(modelVersion, file), sql, Guid.NewGuid(), CancellationToken.None)));
        Assert.That(error!.Message, Is.EqualTo("RUNNER_V9_DEV_SELECTION_REQUIRED"));
        Assert.That(sql.ConnectionRequests, Is.Zero);
    }
}
