using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Read-only operational provenance cardinality diagnostic. A Gold person may
/// legitimately have multiple Silver observations; never infer a 1:1 mapping.
/// Does not authenticate the supplied cohort or authorize productive execution.
/// </summary>
public static class V9OperationalGoldSilverCardinality
{
    public sealed record Receipt(long GoldReferences, long GoldWithActiveResolvedLinks,
        long GoldWithoutActiveResolvedLinks, long GoldWithMultipleObservations,
        long ActiveResolvedObservationLinks);

    public static async Task<Receipt> ReadAsync(
        SqlConnection connection, SqlTransaction snapshotTransaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(snapshotTransaction);
        ct.ThrowIfCancellationRequested();
        if (connection.State != ConnectionState.Open
            || !ReferenceEquals(snapshotTransaction.Connection, connection)
            || snapshotTransaction.IsolationLevel != IsolationLevel.Snapshot)
            throw new InvalidOperationException("RUNNER_V9_OPERATIONAL_SNAPSHOT_REQUIRED");

        const string sql = """
            WITH per_gold AS (
                SELECT g.pessoa_uuid, COUNT_BIG(v.pessoa_observacao_id) AS linked_observations
                FROM gold.pessoa g
                LEFT JOIN identidade.vinculo_fonte v
                  ON v.pessoa_uuid = g.pessoa_uuid
                 AND v.ativo = 1 AND v.status = N'RESOLVIDO'
                LEFT JOIN silver.pessoa_observacao o
                  ON o.pessoa_observacao_id = v.pessoa_observacao_id
                WHERE g.estado_identidade = N'REFERENCIA'
                GROUP BY g.pessoa_uuid
            )
            SELECT COUNT_BIG(*) AS gold_count,
                   COALESCE(SUM(CASE WHEN linked_observations > 0 THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END),0),
                   COALESCE(SUM(CASE WHEN linked_observations = 0 THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END),0),
                   COALESCE(SUM(CASE WHEN linked_observations > 1 THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END),0),
                   COALESCE(SUM(linked_observations),0)
            FROM per_gold;
            """;
        await using var cmd = new SqlCommand(sql, connection, snapshotTransaction) {
            CommandTimeout = 0
        };
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || Enumerable.Range(0, 5).Any(reader.IsDBNull))
            throw new InvalidDataException("RUNNER_V9_PROVENANCE_CARDINALITY_NULL");
        var result = new Receipt(reader.GetInt64(0), reader.GetInt64(1),
            reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4));
        if (await reader.ReadAsync(ct)
            || result.GoldReferences < 0 || result.GoldWithActiveResolvedLinks < 0
            || result.GoldWithoutActiveResolvedLinks < 0
            || result.GoldWithMultipleObservations < 0
            || result.ActiveResolvedObservationLinks < 0
            || result.GoldWithActiveResolvedLinks + result.GoldWithoutActiveResolvedLinks != result.GoldReferences
            || result.GoldWithMultipleObservations > result.GoldWithActiveResolvedLinks
            || result.ActiveResolvedObservationLinks < result.GoldWithActiveResolvedLinks)
            throw new InvalidDataException("RUNNER_V9_PROVENANCE_CARDINALITY_INCONSISTENT");
        return result;
    }
}
