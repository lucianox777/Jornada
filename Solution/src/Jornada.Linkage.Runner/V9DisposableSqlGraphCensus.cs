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

    public static async Task<Receipt> ReadAsync(SqlConnection connection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("RUNNER_V9_SQL_GRAPH_CONNECTION_CLOSED");
        await using (var db = new SqlCommand("SELECT DB_NAME();", connection))
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
            """, connection) { CommandTimeout = 120 })
        {
            await using var reader = await census.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidDataException("RUNNER_V9_SQL_GRAPH_CENSUS_MISSING");
            sqlIds = reader.GetInt64(0);
            sqlEdges = reader.GetInt64(1);
        }

        var ids = new List<long>();
        await using (var command = new SqlCommand(
            "SELECT id FROM #V9CohortIds ORDER BY id;", connection) { CommandTimeout = 120 })
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
