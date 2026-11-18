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
        var graph = await V9OperationalCohortGraphPreview.ReadAsync(
            connection, snapshotTransaction, pinnedRuntime, frozenCohort, ct);
        var verified = V9OperationalGraphReceiptVerification.Verify(
            frozenCohort, graph, independentlyReadGraphCensus);
        if (gold.GoldReferenceCount != verified.ObservationCount)
            throw new InvalidDataException("RUNNER_V9_SNAPSHOT_COHORT_GRAPH_COUNT_MISMATCH");
        return new Receipt(gold, graph, verified);
    }
}
