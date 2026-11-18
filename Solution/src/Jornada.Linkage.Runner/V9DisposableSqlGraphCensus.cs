using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Diagnostic SQL transport for caller-prepared #V9CohortIds(id bigint) and
/// #V9GraphEdges(a bigint,b bigint). No production source adapter or certification.
/// The SQL connection must remain open; only disposable JornadaHarness is allowed.
/// </summary>
public static class V9DisposableSqlGraphCensus
{
    public sealed record Receipt(
        IReadOnlyList<long> ObservationIds,
        IReadOnlyList<(long A, long B)> Edges,
        long SqlObservationRows,
        long SqlEdgeRows);

    /// <summary>
    /// Diagnostic bridge: verifies SQL transport against independently frozen
    /// observation IDs and an externally obtained source census. A caller must
    /// establish source independence and a stable SQL snapshot; this method
    /// cannot certify either condition or authorize productive execution.
    /// </summary>
    public static async Task<V9OperationalCandidateGraphIntegrity.Verified> ReadAndVerifyAsync(
        SqlConnection connection,
        SqlTransaction snapshotTransaction,
        IReadOnlyCollection<long> independentlyFrozenObservationIds,
        V9OperationalCandidateGraphIntegrity.Census independentSourceCensus,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(independentlyFrozenObservationIds);
        ArgumentNullException.ThrowIfNull(independentSourceCensus);
        ValidateSnapshot(connection, snapshotTransaction);
        var receipt = await ReadAsync(connection, snapshotTransaction, ct);
        var expectedIds = independentlyFrozenObservationIds.ToHashSet();
        if (expectedIds.Count != independentlyFrozenObservationIds.Count
            || receipt.ObservationIds.Count != expectedIds.Count
            || receipt.ObservationIds.Any(id => !expectedIds.Contains(id))
            || receipt.ObservationIds.Distinct().Count() != receipt.ObservationIds.Count)
            throw new InvalidDataException("RUNNER_V9_SQL_GRAPH_FROZEN_COHORT_MISMATCH");
        if (receipt.SqlObservationRows != independentSourceCensus.ObservationCount
            || receipt.SqlEdgeRows != independentSourceCensus.EdgeCount)
            throw new InvalidDataException("RUNNER_V9_SQL_GRAPH_SOURCE_CENSUS_MISMATCH");
        return V9OperationalCandidateGraphIntegrity.Verify(
            independentlyFrozenObservationIds, receipt.Edges, independentSourceCensus);
    }

    public static Task<Receipt> ReadAsync(SqlConnection connection, CancellationToken ct) =>
        ReadAsync(connection, null, ct);

    private static void ValidateSnapshot(SqlConnection connection, SqlTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(transaction.Connection, connection)
            || transaction.IsolationLevel != IsolationLevel.Snapshot)
            throw new InvalidOperationException("RUNNER_V9_SQL_GRAPH_SNAPSHOT_REQUIRED");
    }

    private static async Task<Receipt> ReadAsync(
        SqlConnection connection, SqlTransaction? transaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("RUNNER_V9_SQL_GRAPH_CONNECTION_CLOSED");
        await using (var db = new SqlCommand("SELECT DB_NAME();", connection, transaction))
            if (!string.Equals((string?)await db.ExecuteScalarAsync(ct), "JornadaHarness",
                    StringComparison.Ordinal))
                throw new InvalidOperationException("RUNNER_V9_SQL_GRAPH_DIAGNOSTIC_ONLY");

        // Both tables are session-local; never query operational SQL or Gold.
        // A separate upstream adapter must independently attest source parity.
        long sqlIds;
        long sqlEdges;
        await using (var census = new SqlCommand("""
            SELECT (SELECT COUNT_BIG(*) FROM #V9CohortIds),
                   (SELECT COUNT_BIG(*) FROM #V9GraphEdges);
            """, connection, transaction) { CommandTimeout = 120 })
        {
            await using var reader = await census.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidDataException("RUNNER_V9_SQL_GRAPH_CENSUS_MISSING");
            sqlIds = reader.GetInt64(0);
            sqlEdges = reader.GetInt64(1);
        }

        var ids = new List<long>();
        await using (var command = new SqlCommand(
            "SELECT id FROM #V9CohortIds ORDER BY id;", connection, transaction) { CommandTimeout = 120 })
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                ids.Add(reader.GetInt64(0));
                if (ids.Count > sqlIds)
                    throw new InvalidDataException("RUNNER_V9_SQL_GRAPH_ID_CENSUS_MISMATCH");
            }
        }
        if (ids.Count != sqlIds)
            throw new InvalidDataException("RUNNER_V9_SQL_GRAPH_ID_CENSUS_MISMATCH");

        var edges = new List<(long A, long B)>();
        await using (var command = new SqlCommand(
            "SELECT a,b FROM #V9GraphEdges ORDER BY a,b;", connection) { CommandTimeout = 120 })
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                edges.Add((reader.GetInt64(0), reader.GetInt64(1)));
                if (edges.Count > sqlEdges)
                    throw new InvalidDataException("RUNNER_V9_SQL_GRAPH_EDGE_CENSUS_MISMATCH");
            }
        }
        if (edges.Count != sqlEdges)
            throw new InvalidDataException("RUNNER_V9_SQL_GRAPH_EDGE_CENSUS_MISMATCH");
        return new Receipt(ids, edges, sqlIds, sqlEdges);
    }
}
