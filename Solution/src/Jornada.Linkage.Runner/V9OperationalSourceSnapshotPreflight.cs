using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Read-only preflight for actual operational Gold and blocking projection tables.
/// This is NOT a candidate-graph census, cohort freeze, or productive certification.
/// Caller owns the SNAPSHOT transaction and must not commit before subsequent reads.
/// </summary>
public static class V9OperationalSourceSnapshotPreflight
{
    public sealed record Receipt(long GoldReferenceRows, long BlockingKeyRows);

    public static async Task<Receipt> ReadAsync(
        SqlConnection connection, SqlTransaction snapshotTransaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(snapshotTransaction);
        if (connection.State != ConnectionState.Open
            || !ReferenceEquals(snapshotTransaction.Connection, connection)
            || snapshotTransaction.IsolationLevel != IsolationLevel.Snapshot)
            throw new InvalidOperationException("RUNNER_V9_OPERATIONAL_SNAPSHOT_REQUIRED");

        // No NOLOCK, TOP, sampling, fallback or scientific calibration tables.
        // Explicitly fail closed if operational schema is unavailable.
        await using var command = new SqlCommand("""
            SELECT
                (SELECT COUNT_BIG(*) FROM gold.pessoa
                  WHERE estado_identidade = N'REFERENCIA'),
                (SELECT COUNT_BIG(*) FROM identidade.blocking_chave
                  WHERE vigencia_fim IS NULL);
            """, connection, snapshotTransaction) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(0) || reader.IsDBNull(1))
            throw new InvalidDataException("RUNNER_V9_OPERATIONAL_SOURCE_CENSUS_MISSING");
        var gold = reader.GetInt64(0);
        var blocking = reader.GetInt64(1);
        if (gold < 0 || blocking < 0)
            throw new InvalidDataException("RUNNER_V9_OPERATIONAL_SOURCE_CENSUS_INVALID");
        if (await reader.ReadAsync(ct))
            throw new InvalidDataException("RUNNER_V9_OPERATIONAL_SOURCE_CENSUS_DUPLICATE");
        return new Receipt(gold, blocking);
    }
}
