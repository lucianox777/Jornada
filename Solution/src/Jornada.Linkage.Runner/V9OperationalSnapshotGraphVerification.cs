using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Nonpublishing orchestration of operational cohort membership, candidate
/// retrieval and graph integrity within one caller-owned SNAPSHOT transaction.
/// The separate edge census must be supplied by a trusted independent reader.
/// </summary>
public static class V9OperationalSnapshotGraphVerification
{
    public sealed record Receipt(
        V9OperationalGoldCohortMembership.Receipt GoldMembership,
        V9OperationalCohortGraphPreview.Receipt Graph,
        V9OperationalCandidateGraphIntegrity.Verified VerifiedGraph);

    public static async Task<Receipt> VerifyAsync(
        SqlConnection connection,
        SqlTransaction snapshotTransaction,
        V9FittedSnapshotExecutionRuntime pinnedRuntime,
        IReadOnlyCollection<V9OperationalCohortGraphPreview.Source> frozenCohort,
        V9OperationalCandidateGraphIntegrity.Census independentlyReadGraphCensus,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(snapshotTransaction);
        ArgumentNullException.ThrowIfNull(pinnedRuntime);
        ArgumentNullException.ThrowIfNull(frozenCohort);
        ArgumentNullException.ThrowIfNull(independentlyReadGraphCensus);
        ct.ThrowIfCancellationRequested();
        if (connection.State != ConnectionState.Open
            || !ReferenceEquals(snapshotTransaction.Connection, connection)
            || snapshotTransaction.IsolationLevel != IsolationLevel.Snapshot)
            throw new InvalidOperationException("RUNNER_V9_OPERATIONAL_SNAPSHOT_REQUIRED");

        // All operational reads use the SAME connection and SNAPSHOT transaction.
        // A census computed from Graph.Edges is not independent and is forbidden
        // by contract; this API cannot authenticate its external provenance.
        var gold = await V9OperationalGoldCohortMembership.VerifyAsync(
            connection, snapshotTransaction, frozenCohort, ct);
        // Authenticate caller-supplied Silver attributes, manager and active
        // Gold link before any candidate graph construction.
        await V9OperationalSilverSourceAuthentication.VerifyAsync(
            connection, snapshotTransaction, frozenCohort, ct);
        var graph = await V9OperationalCohortGraphPreview.ReadAsync(
            connection, snapshotTransaction, pinnedRuntime, frozenCohort, ct);
        var verified = V9OperationalGraphReceiptVerification.Verify(
            frozenCohort, graph, independentlyReadGraphCensus);
        if (gold.GoldReferenceCount != verified.ObservationCount)
            throw new InvalidDataException("RUNNER_V9_SNAPSHOT_COHORT_GRAPH_COUNT_MISMATCH");
        return new Receipt(gold, graph, verified);
    }
    /// <summary>
    /// Stronger diagnostic entrypoint: obtain the separately sourced edge
    /// census inside the same operational SNAPSHOT before assembling the graph.
    /// The callback must read independently from operational sources, never
    /// from the preview or its receipt. Provenance still needs physical proof.
    /// </summary>
    public static async Task<Receipt> VerifyWithSnapshotCensusAsync(
        SqlConnection connection,
        SqlTransaction snapshotTransaction,
        V9FittedSnapshotExecutionRuntime pinnedRuntime,
        IReadOnlyCollection<V9OperationalCohortGraphPreview.Source> frozenCohort,
        Func<SqlConnection, SqlTransaction, CancellationToken,
            Task<V9OperationalCandidateGraphIntegrity.Census>> readIndependentCensus,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(snapshotTransaction);
        ArgumentNullException.ThrowIfNull(pinnedRuntime);
        ArgumentNullException.ThrowIfNull(frozenCohort);
        ArgumentNullException.ThrowIfNull(readIndependentCensus);
        ct.ThrowIfCancellationRequested();
        if (connection.State != ConnectionState.Open
            || !ReferenceEquals(snapshotTransaction.Connection, connection)
            || snapshotTransaction.IsolationLevel != IsolationLevel.Snapshot)
            throw new InvalidOperationException("RUNNER_V9_OPERATIONAL_SNAPSHOT_REQUIRED");

        // Fetch before the graph is constructed to avoid accidental reuse of
        // its edges. The same connection and transaction are passed explicitly.
        var census = await readIndependentCensus(connection, snapshotTransaction, ct);
        if (census is null)
            throw new InvalidDataException("RUNNER_V9_GRAPH_CENSUS_MISSING");
        ct.ThrowIfCancellationRequested();
        return await VerifyAsync(connection, snapshotTransaction, pinnedRuntime,
            frozenCohort, census, ct);
    }

    /// <summary>
    /// Diagnostic entrypoint wired to the separate operational SQL census
    /// reader. Both readers share one caller-owned SNAPSHOT; no publishing.
    /// Sharing the V9 planner is not independent physical certification.
    /// </summary>
    public static Task<Receipt> VerifyWithOperationalSqlCensusAsync(
        SqlConnection connection,
        SqlTransaction snapshotTransaction,
        V9FittedSnapshotExecutionRuntime pinnedRuntime,
        IReadOnlyCollection<V9OperationalCohortGraphPreview.Source> frozenCohort,
        CancellationToken ct)
    {
        return VerifyWithSnapshotCensusAsync(
            connection, snapshotTransaction, pinnedRuntime, frozenCohort,
            (sameConnection, sameTransaction, token) =>
                V9OperationalSqlEdgeCensus.ReadAsync(
                    sameConnection, sameTransaction, pinnedRuntime, frozenCohort, token),
            ct);
    }

}
