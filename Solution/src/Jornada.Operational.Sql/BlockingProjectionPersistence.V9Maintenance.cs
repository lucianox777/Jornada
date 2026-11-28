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

    /// <summary>Synchronize a pinned V9 projection before scoring in the
    /// caller's SNAPSHOT. The previous receipt authenticates the stored rows
    /// independently of legitimate changes in Gold since the last projection.</summary>
    public static async Task InitializeOrVerifyPinnedV9Async(SqlConnection connection,
        SqlTransaction transaction, string? effectiveEnvironment, bool enabled,
        Guid attemptId, int maximumPrefixLength, string fingerprint, CancellationToken ct)
    {
        await RequireV9WriterAsync(connection, transaction, effectiveEnvironment, enabled,
            attemptId, maximumPrefixLength, fingerprint, ct, allowSnapshot: true);
        var initialized = await IsV9InitializedAsync(connection, transaction, fingerprint, ct);
        var actual = await ReadV9EntriesAsync(connection, transaction, fingerprint, ct);
        await VerifyV9StoredReceiptAsync(connection, transaction, fingerprint, initialized, actual.Count, ct);
        var expected = await ReadExpectedV9EntriesAsync(connection, transaction, maximumPrefixLength, ct);
        if (initialized && expected.SetEquals(actual.Keys))
            return;

        await ReconcileV9EntriesAsync(connection, transaction, fingerprint, expected, actual, ct);
        await WriteV9StoredReceiptAsync(connection, transaction, fingerprint, initialized, ct);
        await VerifyPinnedV9ProjectionAsync(connection, transaction, attemptId,
            maximumPrefixLength, fingerprint, ct);
    }

    /// <summary>Preflight under the existing pipeline lease, before creating
    /// a run. Detect an incomplete/corrupted previous index without treating
    /// legitimate Gold changes as corruption. Never writes projection rows.</summary>
    public static async Task VerifyPinnedV9IndexIntegrityAsync(SqlConnection connection,
        SqlTransaction transaction, string? effectiveEnvironment, bool enabled,
        Guid attemptId, int maximumPrefixLength, string fingerprint, CancellationToken ct)
    {
        await RequireV9WriterAsync(connection, transaction, effectiveEnvironment, enabled,
            attemptId, maximumPrefixLength, fingerprint, ct);
        var initialized = await IsV9InitializedAsync(connection, transaction, fingerprint, ct);
        var actual = await ReadV9EntriesAsync(connection, transaction, fingerprint, ct);
        await VerifyV9StoredReceiptAsync(connection, transaction, fingerprint, initialized, actual.Count, ct);
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
        await VerifyV9StoredReceiptAsync(connection, transaction, fingerprint, true, actual.Count, ct);
        var expected = await ReadExpectedV9EntriesAsync(connection, transaction, maximumPrefixLength, ct);
        if (!expected.SetEquals(actual.Keys))
        {
            await ReconcileV9EntriesAsync(connection, transaction, fingerprint, expected, actual, ct);
            await WriteV9StoredReceiptAsync(connection, transaction, fingerprint, true, ct);
        }
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
        await VerifyV9StoredReceiptAsync(connection, transaction, fingerprint, true, actual.Count, ct);
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
        string fingerprint, CancellationToken ct, bool allowSnapshot = false)
    {
        // C07 precedes all writes, including coverage initialization.
        await V9BlockingMaintenanceGate.RequireAsync(effectiveEnvironment, enabled, attemptId,
            connection, transaction, ct);
        if (transaction.IsolationLevel != IsolationLevel.Serializable
            && !(allowSnapshot && transaction.IsolationLevel == IsolationLevel.Snapshot))
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

    // Physical digest is an internal coverage receipt, not a scientific hash.
    // Include every column and stable row ordering. Never emit nominal data.
    private static async Task<string> ReadV9StoredDigestAsync(SqlConnection connection,
        SqlTransaction transaction, string fingerprint, CancellationToken ct)
    {
        await using var query = new SqlCommand("""
            SELECT CONVERT(char(64), HASHBYTES('SHA2_256', COALESCE((
                SELECT blocking_chave_id,pessoa_uuid,normalizacao_versao,projection_schema_version,
                       projection_fingerprint_sha256,atributo,valor_normalizado,semantica_temporal,
                       vigencia_inicio,vigencia_fim,gerado_em
                  FROM identidade.blocking_chave_v9
                 WHERE normalizacao_versao=@normalization AND projection_schema_version=@schema
                   AND projection_fingerprint_sha256=@fingerprint
                 ORDER BY blocking_chave_id FOR JSON PATH, INCLUDE_NULL_VALUES),N'[]')),2);
            """, connection, transaction) { CommandTimeout = 120 };
        AddV9ProjectionParameters(query, fingerprint);
        return (string?)await query.ExecuteScalarAsync(ct)
            ?? throw new InvalidDataException("RUNNER_V9_BLOCKING_COVERAGE_RECEIPT_MISSING");
    }

    private static async Task VerifyV9StoredReceiptAsync(SqlConnection connection,
        SqlTransaction transaction, string fingerprint, bool initialized, int rowCount, CancellationToken ct)
    {
        if (!initialized)
        {
            // A different pin or rows without an authenticated receipt must
            // never be treated as a new, empty candidate search.
            await using var other = new SqlCommand("""
                SELECT CASE WHEN EXISTS(SELECT 1 FROM identidade.blocking_chave_v9)
                    OR EXISTS(SELECT 1 FROM identidade.blocking_chave_v9_cobertura)
                    THEN 1 ELSE 0 END;
                """, connection, transaction);
            if (rowCount != 0 || Convert.ToInt32(await other.ExecuteScalarAsync(ct),
                    CultureInfo.InvariantCulture) != 0)
                throw new InvalidDataException("RUNNER_V9_BLOCKING_COVERAGE_UNINITIALIZED_WITH_ROWS");
            return;
        }
        await using var receipt = new SqlCommand("""
            SELECT conteudo_sha256 FROM identidade.blocking_chave_v9_cobertura
             WHERE normalizacao_versao=@normalization AND projection_schema_version=@schema
               AND projection_fingerprint_sha256=@fingerprint;
            """, connection, transaction);
        AddV9ProjectionParameters(receipt, fingerprint);
        var stored = (string?)await receipt.ExecuteScalarAsync(ct);
        var actual = await ReadV9StoredDigestAsync(connection, transaction, fingerprint, ct);
        if (!string.Equals(stored, actual, StringComparison.Ordinal))
            throw new InvalidDataException("RUNNER_V9_BLOCKING_COVERAGE_STALE_OR_INCOMPLETE");
    }

    private static async Task WriteV9StoredReceiptAsync(SqlConnection connection,
        SqlTransaction transaction, string fingerprint, bool initialized, CancellationToken ct)
    {
        var digest = await ReadV9StoredDigestAsync(connection, transaction, fingerprint, ct);
        await using var mark = new SqlCommand(initialized ? """
            UPDATE identidade.blocking_chave_v9_cobertura SET conteudo_sha256=@digest
             WHERE normalizacao_versao=@normalization AND projection_schema_version=@schema
               AND projection_fingerprint_sha256=@fingerprint;
            """ : """
            INSERT identidade.blocking_chave_v9_cobertura(
                normalizacao_versao,projection_schema_version,projection_fingerprint_sha256,conteudo_sha256)
            VALUES(@normalization,@schema,@fingerprint,@digest);
            """, connection, transaction);
        AddV9ProjectionParameters(mark, fingerprint);
        mark.Parameters.Add("@digest", SqlDbType.Char, 64).Value = digest;
        if (await mark.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidDataException("RUNNER_V9_BLOCKING_COVERAGE_RECEIPT_MISSING");
    }

    private static async Task<HashSet<V9ProjectionEntry>> ReadExpectedV9EntriesAsync(
        SqlConnection connection, SqlTransaction transaction, int maximumPrefixLength, CancellationToken ct)
    {
        // Read the complete bounded DEV census in two set-based queries. The
        // former per-person loader also read transversal attributes that the
        // frozen V9 projector intentionally discards, and produced an N+1 SQL
        // path during every post-publication reconciliation.
        var people = new List<(Guid Person, BlockingCurrentPerson Current)>();
        await using (var query = new SqlCommand("""
            SELECT g.pessoa_uuid,g.nome_completo,g.nome_mae,g.data_nascimento,
                   g.atualizado_em,p.status
            FROM gold.pessoa g
            JOIN identidade.pessoa p ON p.pessoa_uuid=g.pessoa_uuid
            WHERE g.estado_identidade=N'REFERENCIA' ORDER BY g.pessoa_uuid;
            """, connection, transaction) { CommandTimeout = 120 })
        {
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (!string.Equals(reader.GetString(5), "ATIVO", StringComparison.Ordinal))
                    throw new InvalidDataException("RUNNER_V9_BLOCKING_INACTIVE_GOLD_REFERENCE");
                people.Add((reader.GetGuid(0), new BlockingCurrentPerson(
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : DateOnly.FromDateTime(reader.GetDateTime(3)),
                    reader.GetDateTimeOffset(4))));
            }
        }

        var observations = people.ToDictionary(
            static person => person.Person,
            static _ => new List<BlockingNameObservation>());
        await using (var history = new SqlCommand("""
            SELECT vc.pessoa_uuid,po.nome_completo,po.nome_mae,po.source_as_of
            FROM identidade.v_vinculo_corrente vc
            JOIN gold.pessoa g
              ON g.pessoa_uuid=vc.pessoa_uuid AND g.estado_identidade=N'REFERENCIA'
            JOIN silver.pessoa_observacao po
              ON po.pessoa_observacao_id=vc.pessoa_observacao_id
            WHERE vc.status=N'RESOLVIDO'
            ORDER BY vc.pessoa_uuid,po.source_as_of,po.pessoa_observacao_id;
            """, connection, transaction) { CommandTimeout = 120 })
        {
            await using var reader = await history.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (!observations.TryGetValue(reader.GetGuid(0), out var personObservations))
                    throw new InvalidDataException("RUNNER_V9_BLOCKING_HISTORY_REFERENCE_MISMATCH");
                personObservations.Add(new BlockingNameObservation(
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetDateTimeOffset(3)));
            }
        }

        var expected = new HashSet<V9ProjectionEntry>();
        var supported = V9CalibrationBlockingProjection.SupportedFeatures(maximumPrefixLength)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (person, current) in people)
        {
            ct.ThrowIfCancellationRequested();
            // Canonical projector and historical alias semantics, including
            // CPF-bearing references; no scoring or Gold summary authority.
            var snapshot = BuildSnapshot(current.Name, current.MotherName, current.BirthDate,
                current.AsOf, observations[person], Array.Empty<BlockingDynamicAttributeObservation>(),
                maximumPrefixLength);
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
