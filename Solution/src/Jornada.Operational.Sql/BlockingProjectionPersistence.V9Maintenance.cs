using System.Data;
using System.Globalization;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Operational.Sql;

/// <summary>
/// Reuses the existing Gold/history projector for RF #1936. The isolated
/// table is not identity evidence. No legacy index write or fallback here.
/// A complete census is intentionally used in this bounded DEV phase;
/// no capacity truncation or claim of production-scale performance.
/// </summary>
public static partial class BlockingProjectionPersistence
{
    private sealed record V9ProjectionEntry(Guid PersonUuid, BlockingProjectionRow Key);

    /// <summary>Called before Gold changes, in the same serializable write
    /// transaction. Bootstrap only a never initialized, empty projection.
    /// An existing incomplete projection is an error, not an empty search.</summary>
    public static async Task InitializeOrVerifyPinnedV9Async(SqlConnection connection,
        SqlTransaction transaction, string? effectiveEnvironment, bool enabled,
        Guid attemptId, int maximumPrefixLength, string fingerprint, CancellationToken ct)
    {
        await RequireV9WriterAsync(connection, transaction, effectiveEnvironment, enabled,
            attemptId, maximumPrefixLength, fingerprint, ct);
        var initialized = await IsV9InitializedAsync(connection, transaction, fingerprint, ct);
        var actual = await ReadV9EntriesAsync(connection, transaction, fingerprint, ct);
        var expected = await ReadExpectedV9EntriesAsync(connection, transaction, maximumPrefixLength, ct);
        if (initialized)
        {
            if (!expected.SetEquals(actual.Keys))
                throw new InvalidDataException("RUNNER_V9_BLOCKING_COVERAGE_STALE_OR_INCOMPLETE");
            return;
        }
        if (actual.Count != 0)
            throw new InvalidDataException("RUNNER_V9_BLOCKING_COVERAGE_UNINITIALIZED_WITH_ROWS");

        await ReconcileV9EntriesAsync(connection, transaction, fingerprint, expected, actual, ct);
        await using var mark = new SqlCommand("""
            INSERT identidade.blocking_chave_v9_cobertura(
                normalizacao_versao,projection_schema_version,projection_fingerprint_sha256)
            VALUES(@normalization,@schema,@fingerprint);
            """, connection, transaction);
        AddV9ProjectionParameters(mark, fingerprint);
        if (await mark.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidDataException("RUNNER_V9_BLOCKING_COVERAGE_RECEIPT_MISSING");
        await VerifyPinnedV9ProjectionAsync(connection, transaction, attemptId,
            maximumPrefixLength, fingerprint, ct);
    }

    /// <summary>Called after real Gold recomposition and before publication
    /// commit. Same C07 gate, transaction and projection. Reconcile additions
    /// and invalidations, retaining unchanged physical rows and timestamps.</summary>
    public static async Task RefreshPinnedV9Async(SqlConnection connection,
        SqlTransaction transaction, string? effectiveEnvironment, bool enabled,
        Guid attemptId, int maximumPrefixLength, string fingerprint, CancellationToken ct)
    {
        await RequireV9WriterAsync(connection, transaction, effectiveEnvironment, enabled,
            attemptId, maximumPrefixLength, fingerprint, ct);
        if (!await IsV9InitializedAsync(connection, transaction, fingerprint, ct))
            throw new InvalidDataException("RUNNER_V9_BLOCKING_COVERAGE_RECEIPT_MISSING");
        var actual = await ReadV9EntriesAsync(connection, transaction, fingerprint, ct);
        var expected = await ReadExpectedV9EntriesAsync(connection, transaction, maximumPrefixLength, ct);
        await ReconcileV9EntriesAsync(connection, transaction, fingerprint, expected, actual, ct);
        await VerifyPinnedV9ProjectionAsync(connection, transaction, attemptId,
            maximumPrefixLength, fingerprint, ct);
    }

    /// <summary>Read-only exact coverage/freshness proof, in the same snapshot
    /// used to retrieve candidates. Missing, stale or extra keys fail closed.</summary>
    public static async Task VerifyPinnedV9ProjectionAsync(SqlConnection connection,
        SqlTransaction transaction, Guid attemptId, int maximumPrefixLength,
        string fingerprint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.IsolationLevel is not (IsolationLevel.Snapshot or IsolationLevel.Serializable))
            throw new InvalidOperationException("RUNNER_V9_BLOCKING_CONSISTENT_TRANSACTION_REQUIRED");
        ValidateV9Projection(maximumPrefixLength, fingerprint);
        await V9DisposableAttemptDatabase.VerifyAsync(connection, attemptId, ct, transaction);
        if (!await IsV9InitializedAsync(connection, transaction, fingerprint, ct))
            throw new InvalidDataException("RUNNER_V9_BLOCKING_COVERAGE_RECEIPT_MISSING");
        var expected = await ReadExpectedV9EntriesAsync(connection, transaction, maximumPrefixLength, ct);
        var actual = await ReadV9EntriesAsync(connection, transaction, fingerprint, ct);
        if (!expected.SetEquals(actual.Keys))
            throw new InvalidDataException("RUNNER_V9_BLOCKING_COVERAGE_STALE_OR_INCOMPLETE");
    }

    private static void ValidateV9Projection(int maximumPrefixLength, string fingerprint)
    {
        V9CalibrationBlockingProjection.ValidateMaximumPrefixLength(maximumPrefixLength);
        if (!string.Equals(V9CalibrationBlockingProjection.ComputeSchemaFingerprint(maximumPrefixLength),
                fingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("RUNNER_V9_BLOCKING_PROJECTION_PIN_MISMATCH");
    }

    private static async Task RequireV9WriterAsync(SqlConnection connection, SqlTransaction transaction,
        string? effectiveEnvironment, bool enabled, Guid attemptId, int maximumPrefixLength,
        string fingerprint, CancellationToken ct)
    {
        // C07 precedes all writes, including coverage initialization.
        await V9BlockingMaintenanceGate.RequireAsync(effectiveEnvironment, enabled, attemptId,
            connection, transaction, ct);
        if (transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("RUNNER_V9_BLOCKING_SERIALIZABLE_REQUIRED");
        ValidateV9Projection(maximumPrefixLength, fingerprint);
        // Serialize cooperating writers BEFORE they mutate Gold. The schema
        // lock from C07 remains shared until the enclosing transaction ends.
        await using var acquire = new SqlCommand("""
            DECLARE @lock_result int;
            EXEC @lock_result=sys.sp_getapplock @Resource=N'Jornada.V9.BlockingMaintenance',
                @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=60000;
            SELECT @lock_result;
            """, connection, transaction) { CommandTimeout = 65 };
        if (Convert.ToInt32(await acquire.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) < 0)
            throw new InvalidOperationException("RUNNER_V9_BLOCKING_WRITER_LOCK_UNAVAILABLE");
    }

    private static void AddV9ProjectionParameters(SqlCommand command, string fingerprint)
    {
        command.Parameters.Add("@normalization", SqlDbType.NVarChar, 80).Value = IdentityComparison.NormalizationVersion;
        command.Parameters.Add("@schema", SqlDbType.NVarChar, 120).Value = V9CalibrationBlockingProjection.SchemaVersion;
        command.Parameters.Add("@fingerprint", SqlDbType.Char, 64).Value = fingerprint;
    }

    private static async Task<bool> IsV9InitializedAsync(SqlConnection connection,
        SqlTransaction transaction, string fingerprint, CancellationToken ct)
    {
        await using (var schema = new SqlCommand("""
            SELECT CASE WHEN OBJECT_ID(N'identidade.blocking_chave_v9',N'U') IS NOT NULL
               AND OBJECT_ID(N'identidade.blocking_chave_v9_cobertura',N'U') IS NOT NULL
               AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0
                   AND name=N'Jornada.V9.BlockingSchemaEnabled'
                   AND CONVERT(nvarchar(10),value)=N'1') THEN 1 ELSE 0 END;
            """, connection, transaction))
        {
            if (Convert.ToInt32(await schema.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) != 1)
                throw new InvalidOperationException("RUNNER_V9_BLOCKING_SCHEMA_NOT_AVAILABLE");
        }
        await using var query = new SqlCommand("""
            SELECT COUNT_BIG(*) FROM identidade.blocking_chave_v9_cobertura
             WHERE normalizacao_versao=@normalization AND projection_schema_version=@schema
               AND projection_fingerprint_sha256=@fingerprint;
            """, connection, transaction);
        AddV9ProjectionParameters(query, fingerprint);
        return Convert.ToInt64(await query.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<HashSet<V9ProjectionEntry>> ReadExpectedV9EntriesAsync(
        SqlConnection connection, SqlTransaction transaction, int maximumPrefixLength, CancellationToken ct)
    {
        var people = new List<Guid>();
        await using (var query = new SqlCommand("""
            SELECT g.pessoa_uuid,p.status FROM gold.pessoa g
            JOIN identidade.pessoa p ON p.pessoa_uuid=g.pessoa_uuid
            WHERE g.estado_identidade=N'REFERENCIA' ORDER BY g.pessoa_uuid;
            """, connection, transaction))
        {
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (!string.Equals(reader.GetString(1), "ATIVO", StringComparison.Ordinal))
                    throw new InvalidDataException("RUNNER_V9_BLOCKING_INACTIVE_GOLD_REFERENCE");
                people.Add(reader.GetGuid(0));
            }
        }
        var expected = new HashSet<V9ProjectionEntry>();
        var supported = V9CalibrationBlockingProjection.SupportedFeatures(maximumPrefixLength)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var person in people)
        {
            ct.ThrowIfCancellationRequested();
            // Canonical projector and historical alias semantics, including
            // CPF-bearing references; no scoring or Gold summary authority.
            var snapshot = await LoadSqlServerSnapshotAsync(connection, transaction,
                person, ct, maximumPrefixLength);
            foreach (var row in snapshot.Rows)
            {
                if (!supported.Contains(row.Feature) || string.IsNullOrWhiteSpace(row.Value)
                    || row.Value.Length > 500 || row.Feature.Length > 80
                    || (row.ValidTo is { } end && end < row.ValidFrom)
                    || !expected.Add(new V9ProjectionEntry(person, row)))
                    throw new InvalidDataException("RUNNER_V9_BLOCKING_EXPECTED_KEY_INVALID");
            }
        }
        return expected;
    }

    private static async Task<Dictionary<V9ProjectionEntry, long>> ReadV9EntriesAsync(
        SqlConnection connection, SqlTransaction transaction, string fingerprint, CancellationToken ct)
    {
        await using var query = new SqlCommand("""
            SELECT blocking_chave_id,pessoa_uuid,atributo,valor_normalizado,semantica_temporal,
                   vigencia_inicio,vigencia_fim
              FROM identidade.blocking_chave_v9
             WHERE normalizacao_versao=@normalization AND projection_schema_version=@schema
               AND projection_fingerprint_sha256=@fingerprint;
            """, connection, transaction) { CommandTimeout = 120 };
        AddV9ProjectionParameters(query, fingerprint);
        var actual = new Dictionary<V9ProjectionEntry, long>();
        await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var entry = new V9ProjectionEntry(reader.GetGuid(1), new BlockingProjectionRow(
                reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetDateTimeOffset(5), reader.IsDBNull(6) ? null : reader.GetDateTimeOffset(6)));
            if (!actual.TryAdd(entry, reader.GetInt64(0)))
                throw new InvalidDataException("RUNNER_V9_BLOCKING_DUPLICATE_KEY");
        }
        return actual;
    }

    private static async Task ReconcileV9EntriesAsync(SqlConnection connection,
        SqlTransaction transaction, string fingerprint, HashSet<V9ProjectionEntry> expected,
        Dictionary<V9ProjectionEntry, long> actual, CancellationToken ct)
    {
        foreach (var pair in actual.Where(pair => !expected.Contains(pair.Key)))
        {
            await using var remove = new SqlCommand("""
                DELETE FROM identidade.blocking_chave_v9
                 WHERE blocking_chave_id=@id AND normalizacao_versao=@normalization
                   AND projection_schema_version=@schema AND projection_fingerprint_sha256=@fingerprint;
                """, connection, transaction);
            AddV9ProjectionParameters(remove, fingerprint);
            remove.Parameters.Add("@id", SqlDbType.BigInt).Value = pair.Value;
            if (await remove.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidDataException("RUNNER_V9_BLOCKING_INVALIDATION_MISMATCH");
        }
        foreach (var entry in expected.Where(entry => !actual.ContainsKey(entry)))
        {
            var row = entry.Key;
            await using var insert = new SqlCommand("""
                INSERT identidade.blocking_chave_v9(
                    pessoa_uuid,normalizacao_versao,projection_schema_version,
                    projection_fingerprint_sha256,atributo,valor_normalizado,
                    semantica_temporal,vigencia_inicio,vigencia_fim)
                VALUES(@person,@normalization,@schema,@fingerprint,@feature,@value,@temporal,@from,@to);
                """, connection, transaction);
            AddV9ProjectionParameters(insert, fingerprint);
            insert.Parameters.Add("@person", SqlDbType.UniqueIdentifier).Value = entry.PersonUuid;
            insert.Parameters.Add("@feature", SqlDbType.NVarChar, 80).Value = row.Feature;
            insert.Parameters.Add("@value", SqlDbType.NVarChar, 500).Value = row.Value;
            insert.Parameters.Add("@temporal", SqlDbType.NVarChar, 30).Value = row.TemporalSemantics;
            insert.Parameters.Add("@from", SqlDbType.DateTimeOffset).Value = row.ValidFrom;
            insert.Parameters.Add("@to", SqlDbType.DateTimeOffset).Value = (object?)row.ValidTo ?? DBNull.Value;
            if (await insert.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidDataException("RUNNER_V9_BLOCKING_INSERT_MISMATCH");
        }
    }
}
