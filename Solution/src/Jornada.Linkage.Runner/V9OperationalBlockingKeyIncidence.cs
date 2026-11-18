using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Operational read-only diagnostic of active blocking-key incidence.
/// This does not construct or certify the complete V9 candidate graph.
/// </summary>
public static class V9OperationalBlockingKeyIncidence
{
    public sealed record Receipt(long ActiveKeys, long KeysWithLiveGoldReference,
        long KeysWithoutLiveGoldReference);

    public static async Task<Receipt> ReadAsync(
        SqlConnection connection, SqlTransaction snapshotTransaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(snapshotTransaction);
        if (connection.State != ConnectionState.Open
            || !ReferenceEquals(snapshotTransaction.Connection, connection)
            || snapshotTransaction.IsolationLevel != IsolationLevel.Snapshot)
            throw new InvalidOperationException("RUNNER_V9_OPERATIONAL_SNAPSHOT_REQUIRED");

        // COUNT_BIG over the entire operational source, not a paged sample.
        // NOT EXISTS avoids multiplying blocking keys by duplicate Gold rows.
        await using var command = new SqlCommand("""
            SELECT
                (SELECT COUNT_BIG(*) FROM identidade.blocking_chave b
                 WHERE b.vigencia_fim IS NULL),
                (SELECT COUNT_BIG(*) FROM identidade.blocking_chave b
                 WHERE b.vigencia_fim IS NULL
                   AND EXISTS (SELECT 1 FROM gold.pessoa g
                               WHERE g.pessoa_uuid = b.pessoa_uuid
                                 AND g.estado_identidade = N'REFERENCIA')),
                (SELECT COUNT_BIG(*) FROM identidade.blocking_chave b
                 WHERE b.vigencia_fim IS NULL
                   AND NOT EXISTS (SELECT 1 FROM gold.pessoa g
                                   WHERE g.pessoa_uuid = b.pessoa_uuid
                                     AND g.estado_identidade = N'REFERENCIA'));
            """, connection, snapshotTransaction) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(0)
            || reader.IsDBNull(1) || reader.IsDBNull(2))
            throw new InvalidDataException("RUNNER_V9_OPERATIONAL_KEY_INCIDENCE_MISSING");
        var total = reader.GetInt64(0);
        var matched = reader.GetInt64(1);
        var unmatched = reader.GetInt64(2);
        if (total < 0 || matched < 0 || unmatched < 0
            || matched > total || unmatched > total || matched != total - unmatched)
            throw new InvalidDataException("RUNNER_V9_OPERATIONAL_KEY_INCIDENCE_INVALID");
        if (await reader.ReadAsync(ct))
            throw new InvalidDataException("RUNNER_V9_OPERATIONAL_KEY_INCIDENCE_DUPLICATE");
        return new Receipt(total, matched, unmatched);
    }
}
