using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Complete, read-only operational blocking projection manifest under caller-owned SNAPSHOT.
/// This is evidence of source keys, not V9 candidate graph completeness or certification.
/// </summary>
public static class V9OperationalBlockingManifest
{
    public sealed record Receipt(long ActiveKeyRows, string CanonicalManifestSha256);

    // Preserve the existing diagnostic/legacy manifest path. Only the
    // explicitly pinned operational V9 consumer uses the isolated table.
    public static Task<Receipt> ReadAsync(
        SqlConnection connection, SqlTransaction snapshotTransaction, CancellationToken ct) =>
        ReadCoreAsync(connection, snapshotTransaction, null, ct);

    public static Task<Receipt> ReadIsolatedAsync(SqlConnection connection,
        SqlTransaction snapshotTransaction, V9FittedSnapshotExecutionRuntime pinnedRuntime, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pinnedRuntime);
        if (pinnedRuntime.MaximumPrefixLength is null)
            throw new InvalidDataException("RUNNER_V9_BLOCKING_PROJECTION_PIN_MISMATCH");
        return ReadCoreAsync(connection, snapshotTransaction, pinnedRuntime, ct);
    }

    private static void BindIsolatedProjection(SqlCommand command, V9FittedSnapshotExecutionRuntime? pin)
    {
        if (pin is null)
            return;
        // Fixed table token and parameterized projection identity. Never a
        // fallback to legacy or a table name supplied by the caller.
        command.CommandText = command.CommandText
            .Replace("identidade.blocking_chave", "identidade.blocking_chave_v9", StringComparison.Ordinal)
            .Replace("WHERE vigencia_fim IS NULL",
                "WHERE vigencia_fim IS NULL AND normalizacao_versao=@normalization " +
                "AND projection_schema_version=@schema AND projection_fingerprint_sha256=@fingerprint",
                StringComparison.Ordinal);
        command.Parameters.Add("@normalization", SqlDbType.NVarChar, 80).Value = IdentityComparison.NormalizationVersion;
        command.Parameters.Add("@schema", SqlDbType.NVarChar, 120).Value = pin.ProjectionSchemaVersion;
        command.Parameters.Add("@fingerprint", SqlDbType.Char, 64).Value = pin.ProjectionFingerprintSha256;
    }

    private static async Task<Receipt> ReadCoreAsync(SqlConnection connection,
        SqlTransaction snapshotTransaction, V9FittedSnapshotExecutionRuntime? pin, CancellationToken ct)
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
        {
            BindIsolatedProjection(census, pin);
            expected = Convert.ToInt64(await census.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        }
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
        BindIsolatedProjection(query, pin);
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
