using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Unit;

/// <summary>
/// Input/connection regression for the real operational SQL candidate reader.
/// No fake Gold candidates and no scientific TEST execution are introduced.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class V9ControlledDevOperationalCandidateSourceTests
{
    private static readonly V9OperationalPinnedCohortRuntime Pinned =
        V9OperationalPinnedCohortRuntime.FromVerifiedFileSnapshot(
            V9FittedSnapshotTests.CompleteTf4OperationalWinner());

    private static V9FrozenRunObservation Row(long id, string? cpf = null) =>
        new(id, new IdentityObservation(cpf,
            cpf is null ? "SEM_CPF" : null,
            "ANA PAULA SILVA", new DateOnly(1990, 1, 1), "MARIA SILVA"));

    [Test]
    public void Malformed_frozen_universe_fails_before_any_SQL_candidate_query()
    {
        using var closed = new SqlConnection();
        async Task ExecuteAsync(IReadOnlyList<V9FrozenRunObservation> rows) =>
            await V9ControlledDevOperationalCandidateSource.ReadAsync(
                closed, Pinned, Guid.NewGuid(), rows, 10, CancellationToken.None);

        foreach (var bad in new IReadOnlyList<V9FrozenRunObservation>[]
        {
            [Row(2), Row(1)],
            [Row(1), Row(1)],
            [Row(0)],
            [Row(1, "12345678901")]
        })
            Assert.That(Assert.ThrowsAsync<InvalidDataException>(
                (Func<Task>)(() => ExecuteAsync(bad)))!.Message,
                Is.EqualTo("RUNNER_V9_DEV_FROZEN_UNIVERSE_INVALID"));

        Assert.That(closed.State, Is.EqualTo(System.Data.ConnectionState.Closed));
    }

    [Test]
    public void Missing_attempt_and_nonpositive_candidate_capacity_fail_before_SQL()
    {
        using var closed = new SqlConnection();
        var rows = new[] { Row(1) };
        Assert.That(Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
            V9ControlledDevOperationalCandidateSource.ReadAsync(
                closed, Pinned, Guid.Empty, rows, 10, CancellationToken.None)))!.Message,
            Is.EqualTo("RUNNER_V9_DEV_ATTEMPT_MISSING"));
        Assert.That(Assert.ThrowsAsync<ArgumentOutOfRangeException>((Func<Task>)(() =>
            V9ControlledDevOperationalCandidateSource.ReadAsync(
                closed, Pinned, Guid.NewGuid(), rows, 0, CancellationToken.None)))!.ParamName,
            Is.EqualTo("maximumCandidatesPerObservation"));
        Assert.That(Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
            V9ControlledDevOperationalCandidateSource.ReadAsync(
                closed, Pinned, Guid.NewGuid(), rows, 10, CancellationToken.None)))!.Message,
            Is.EqualTo("RUNNER_V9_DEV_DISPOSABLE_SQL_NOT_VERIFIED"));
        Assert.That(closed.State, Is.EqualTo(System.Data.ConnectionState.Closed));
    }
}
