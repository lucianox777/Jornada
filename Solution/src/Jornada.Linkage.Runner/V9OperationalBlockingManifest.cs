using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Complete, read-only operational blocking projection manifest under caller-owned SNAPSHOT.
/// This is evidence of source keys, not V9 candidate graph completeness or certification.
/// </summary>
public static class V9OperationalBlockingManifest
{
    public sealed record Receipt(long ActiveKeyRows, string CanonicalManifestSha256);

    public static async Task<Receipt> ReadAsync(
        SqlConnection connection, SqlTransaction snapshotTransaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(snapshotTransaction);
        if (connection.State != ConnectionState.Open
            || !ReferenceEquals(snapshotTransaction.Connection, connection)
            || snapshotTransaction.IsolationLevel != IsolationLevel.Snapshot)
            throw new InvalidOperationException("RUNNER_V9_OPERATIONAL_SNAPSHOT_REQUIRED");

        long expected;
        await using (var census = new SqlCommand(
            "SELECT COUNT_BIG(*) FROM identidade.blocking_chave WHERE vigencia_fim IS NULL;",
            connection, snapshotTransaction) { CommandTimeout = 120 })
            expected = Convert.ToInt64(await census.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        if (expected < 0)
            throw new InvalidDataException("RUNNER_V9_BLOCKING_MANIFEST_CENSUS_INVALID");

        // Binary ordering prevents collation-dependent changes to the receipt.
        // No TOP/OFFSET or in-memory limit: every active key contributes.
        await using var query = new SqlCommand("""
            SELECT pessoa_uuid, atributo, valor_normalizado, normalizacao_versao,
                   projection_schema_version, projection_fingerprint_sha256,
                   semantica_temporal
              FROM identidade.blocking_chave
             WHERE vigencia_fim IS NULL
             ORDER BY pessoa_uuid,
                      atributo COLLATE Latin1_General_100_BIN2,
                      valor_normalizado COLLATE Latin1_General_100_BIN2,
                      normalizacao_versao COLLATE Latin1_General_100_BIN2,
                      projection_schema_version COLLATE Latin1_General_100_BIN2,
                      projection_fingerprint_sha256 COLLATE Latin1_General_100_BIN2,
                      semantica_temporal COLLATE Latin1_General_100_BIN2;
            """, connection, snapshotTransaction) { CommandTimeout = 0 };
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long seen = 0;
        string? previous = null;
        await using var reader = await query.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess, ct);
        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            if (reader.IsDBNull(0) || Enumerable.Range(1, 6).Any(reader.IsDBNull))
                throw new InvalidDataException("RUNNER_V9_BLOCKING_MANIFEST_NULL");
            // Length-prefixed fields make the digest unambiguous even when
            // source values contain separators or line breaks.
            var fields = new[] {
                reader.GetGuid(0).ToString("D"),
                reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6)
            };
            var canonical = string.Concat(fields.Select(value =>
                Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture)
                + ":" + value)) + "\n";
            if (previous is not null && string.Equals(previous, canonical, StringComparison.Ordinal))
                throw new InvalidDataException("RUNNER_V9_BLOCKING_MANIFEST_DUPLICATE");
            previous = canonical;
            sha.AppendData(Encoding.UTF8.GetBytes(canonical));
            seen = checked(seen + 1);
            if (seen > expected)
                throw new InvalidDataException("RUNNER_V9_BLOCKING_MANIFEST_COUNT_MISMATCH");
        }
        if (seen != expected)
            throw new InvalidDataException("RUNNER_V9_BLOCKING_MANIFEST_COUNT_MISMATCH");
        return new Receipt(seen, Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant());
    }
}
