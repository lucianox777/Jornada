using System.Data;
using System.Globalization;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Operational.Sql;

/// <summary>RF #1936: shared projection writer. No writes to the legacy index.</summary>
public static partial class BlockingProjectionPersistence
{
    // The plan carries only the projection identity of the already validated
    // file-pinned winner. It does not confer write authority.
    public sealed record V9MaintenancePlan(string EffectiveEnvironment, bool Enabled,
        Guid AttemptId, string ProjectionFingerprintSha256);

    private static int VerifyMaintenanceProjection(string fingerprint) =>
        V9CalibrationBlockingProjection.ResolveFrozenPrefixLength(
            V9CalibrationBlockingProjection.SchemaVersion, fingerprint);

    private static async Task RequireV9MaintenanceWriterAsync(SqlConnection db,
        SqlTransaction tx, V9MaintenancePlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await V9BlockingMaintenanceGate.RequireAsync(plan.EffectiveEnvironment, plan.Enabled,
            plan.AttemptId, db, tx, ct);
        if (tx.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("RUNNER_V9_BLOCKING_SERIALIZABLE_REQUIRED");
        // Serialize initialization and refresh, including concurrent Processor
        // batches. This lock is held until the caller's publication commits.
        await using var cmd = new SqlCommand("""
            DECLARE @r int;
            EXEC @r=sys.sp_getapplock @Resource=N'Jornada.V9.BlockingMaintenance',
                @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
            SELECT @r;
            """, db, tx);
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) < 0)
            throw new InvalidOperationException("RUNNER_V9_BLOCKING_MAINTENANCE_LOCK");
    }

    private static async Task<List<Guid>> ReadV9ReferenceIdsAsync(
        SqlConnection db, SqlTransaction tx, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT g.pessoa_uuid FROM gold.pessoa g
            JOIN identidade.pessoa p ON p.pessoa_uuid=g.pessoa_uuid
            WHERE g.estado_identidade=N'REFERENCIA' AND p.status=N'ATIVO'
            ORDER BY g.pessoa_uuid;
            """, db, tx);
        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetGuid(0));
        return ids;
    }

    private static async Task<bool> HasV9CoverageAsync(SqlConnection db, SqlTransaction tx,
        string fingerprint, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT COUNT_BIG(*) FROM identidade.blocking_chave_v9_cobertura
             WHERE projection_fingerprint_sha256=@fingerprint;
            """, db, tx);
        cmd.Parameters.Add("@fingerprint", SqlDbType.Char, 64).Value = fingerprint;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 1;
    }

    /// <summary>
    /// Initialize exactly once per projection, inside the producer transaction.
    /// Existing incomplete indexes are NOT silently repaired on a read.
    /// </summary>
    public static async Task EnsureV9CoverageInitializedAsync(SqlConnection db, SqlTransaction tx,
        V9MaintenancePlan plan, CancellationToken ct)
    {
        await RequireV9MaintenanceWriterAsync(db, tx, plan, ct);
        var prefix = VerifyMaintenanceProjection(plan.ProjectionFingerprintSha256);
        if (await HasV9CoverageAsync(db, tx, plan.ProjectionFingerprintSha256, ct))
            return;
        // A nonempty, unregistered index is an inconsistency, not permission
        // to discard data or to pretend that initialization was successful.
        await using (var census = new SqlCommand("""
            SELECT COUNT_BIG(*) FROM identidade.blocking_chave_v9
             WHERE projection_fingerprint_sha256=@fingerprint;
            """, db, tx))
        {
            census.Parameters.Add("@fingerprint", SqlDbType.Char, 64).Value = plan.ProjectionFingerprintSha256;
            if (Convert.ToInt64(await census.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) != 0)
                throw new InvalidDataException("RUNNER_V9_BLOCKING_UNREGISTERED_ROWS");
        }
        foreach (var id in await ReadV9ReferenceIdsAsync(db, tx, ct))
            await ReplaceV9ReferenceAsync(db, tx, id, prefix, plan.ProjectionFingerprintSha256, ct);
        await using var marker = new SqlCommand("""
            INSERT identidade.blocking_chave_v9_cobertura(projection_fingerprint_sha256,inicializado_em)
            VALUES(@fingerprint,SYSDATETIMEOFFSET());
            """, db, tx);
        marker.Parameters.Add("@fingerprint", SqlDbType.Char, 64).Value = plan.ProjectionFingerprintSha256;
        if (await marker.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidDataException("RUNNER_V9_BLOCKING_COVERAGE_WRITE_FAILED");
    }

    public static async Task RefreshV9ReferencesAsync(SqlConnection db, SqlTransaction tx,
        V9MaintenancePlan plan, IReadOnlyCollection<Guid> affected, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(affected);
        await RequireV9MaintenanceWriterAsync(db, tx, plan, ct);
        var prefix = VerifyMaintenanceProjection(plan.ProjectionFingerprintSha256);
        if (!await HasV9CoverageAsync(db, tx, plan.ProjectionFingerprintSha256, ct))
            throw new InvalidDataException("RUNNER_V9_BLOCKING_COVERAGE_MISSING");
        foreach (var id in affected.Distinct().OrderBy(x => x))
        {
            if (id == Guid.Empty)
                throw new InvalidDataException("RUNNER_V9_BLOCKING_REFERENCE_INVALID");
            await ReplaceV9ReferenceAsync(db, tx, id, prefix, plan.ProjectionFingerprintSha256, ct);
        }
    }

    private static async Task<BlockingProjectionSnapshot> LoadV9EligibleSnapshotAsync(
        SqlConnection db, SqlTransaction tx, Guid id, int prefix, CancellationToken ct)
    {
        await using var check = new SqlCommand("""
            SELECT COUNT_BIG(*) FROM identidade.pessoa WHERE pessoa_uuid=@id AND status=N'ATIVO';
            """, db, tx);
        check.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        if (Convert.ToInt64(await check.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) != 1)
            return BlockingProjectionSnapshot.Empty;
        return await LoadSqlServerSnapshotAsync(db, tx, id, ct, prefix);
    }

    private static async Task<List<BlockingProjectionRow>> ReadV9RowsAsync(SqlConnection db,
        SqlTransaction tx, Guid id, string fingerprint, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT atributo,valor_normalizado,semantica_temporal,vigencia_inicio,vigencia_fim,
                   normalizacao_versao,projection_schema_version
              FROM identidade.blocking_chave_v9
             WHERE pessoa_uuid=@id AND projection_fingerprint_sha256=@fingerprint;
            """, db, tx);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        cmd.Parameters.Add("@fingerprint", SqlDbType.Char, 64).Value = fingerprint;
        var rows = new List<BlockingProjectionRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (reader.GetString(5) != IdentityComparison.NormalizationVersion
                || reader.GetString(6) != V9CalibrationBlockingProjection.SchemaVersion)
                throw new InvalidDataException("RUNNER_V9_BLOCKING_CONTRACT_MISMATCH");
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetDateTimeOffset(3), reader.IsDBNull(4) ? null : reader.GetDateTimeOffset(4)));
        }
        return rows;
    }

    private static bool SameV9Rows(IReadOnlyCollection<BlockingProjectionRow> expected,
        IReadOnlyCollection<BlockingProjectionRow> actual) =>
        expected.Count == actual.Count && expected.ToHashSet().SetEquals(actual);

    private static async Task ReplaceV9ReferenceAsync(SqlConnection db, SqlTransaction tx,
        Guid id, int prefix, string fingerprint, CancellationToken ct)
    {
        var expected = await LoadV9EligibleSnapshotAsync(db, tx, id, prefix, ct);
        var actual = await ReadV9RowsAsync(db, tx, id, fingerprint, ct);
        if (SameV9Rows(expected.Rows, actual))
            return; // identical reprocessing must not rewrite physical rows
        await using (var delete = new SqlCommand("""
            DELETE FROM identidade.blocking_chave_v9
             WHERE pessoa_uuid=@id AND projection_fingerprint_sha256=@fingerprint;
            """, db, tx))
        {
            delete.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
            delete.Parameters.Add("@fingerprint", SqlDbType.Char, 64).Value = fingerprint;
            await delete.ExecuteNonQueryAsync(ct);
        }
        foreach (var row in expected.Rows)
        {
            await using var insert = new SqlCommand("""
                INSERT identidade.blocking_chave_v9(pessoa_uuid,normalizacao_versao,
                    projection_schema_version,projection_fingerprint_sha256,atributo,
                    valor_normalizado,semantica_temporal,vigencia_inicio,vigencia_fim)
                VALUES(@id,@normalization,@schema,@fingerprint,@feature,@value,@temporal,@from,@to);
                """, db, tx);
            insert.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
            insert.Parameters.Add("@normalization", SqlDbType.NVarChar, 80).Value = IdentityComparison.NormalizationVersion;
            insert.Parameters.Add("@schema", SqlDbType.NVarChar, 120).Value = V9CalibrationBlockingProjection.SchemaVersion;
            insert.Parameters.Add("@fingerprint", SqlDbType.Char, 64).Value = fingerprint;
            insert.Parameters.Add("@feature", SqlDbType.NVarChar, 80).Value = row.Feature;
            insert.Parameters.Add("@value", SqlDbType.NVarChar, 500).Value = row.Value;
            insert.Parameters.Add("@temporal", SqlDbType.NVarChar, 30).Value = row.TemporalSemantics;
            insert.Parameters.Add("@from", SqlDbType.DateTimeOffset).Value = row.ValidFrom;
            insert.Parameters.Add("@to", SqlDbType.DateTimeOffset).Value = (object?)row.ValidTo ?? DBNull.Value;
            if (await insert.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidDataException("RUNNER_V9_BLOCKING_WRITE_FAILED");
        }
        if (!SameV9Rows(expected.Rows, await ReadV9RowsAsync(db, tx, id, fingerprint, ct)))
            throw new InvalidDataException("RUNNER_V9_BLOCKING_WRITE_MISMATCH");
    }

    /// <summary>
    /// Before using the isolated index, authenticate coverage AND all physical
    /// rows against the existing projector in the very same read SNAPSHOT.
    /// Expensive correctness-first baseline; no truncation, no automatic repair.
    /// </summary>
    public static async Task VerifyV9CoverageAsync(SqlConnection db, SqlTransaction snapshot,
        V9MaintenancePlan plan, CancellationToken ct)
    {
        if (snapshot.IsolationLevel != IsolationLevel.Snapshot)
            throw new InvalidOperationException("RUNNER_V9_BLOCKING_SNAPSHOT_REQUIRED");
        await V9BlockingMaintenanceGate.RequireAsync(plan.EffectiveEnvironment, plan.Enabled,
            plan.AttemptId, db, snapshot, ct);
        var prefix = VerifyMaintenanceProjection(plan.ProjectionFingerprintSha256);
        if (!await HasV9CoverageAsync(db, snapshot, plan.ProjectionFingerprintSha256, ct))
            throw new InvalidDataException("RUNNER_V9_BLOCKING_COVERAGE_MISSING");
        var ids = await ReadV9ReferenceIdsAsync(db, snapshot, ct);
        long expectedCount = 0;
        foreach (var id in ids)
        {
            var expected = await LoadV9EligibleSnapshotAsync(db, snapshot, id, prefix, ct);
            var actual = await ReadV9RowsAsync(db, snapshot, id, plan.ProjectionFingerprintSha256, ct);
            if (!SameV9Rows(expected.Rows, actual))
                throw new InvalidDataException("RUNNER_V9_BLOCKING_COVERAGE_STALE");
            expectedCount = checked(expectedCount + expected.Rows.Count);
        }
        await using var census = new SqlCommand("""
            SELECT COUNT_BIG(*) FROM identidade.blocking_chave_v9
             WHERE projection_fingerprint_sha256=@fingerprint;
            """, db, snapshot);
        census.Parameters.Add("@fingerprint", SqlDbType.Char, 64).Value = plan.ProjectionFingerprintSha256;
        if (Convert.ToInt64(await census.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) != expectedCount)
            throw new InvalidDataException("RUNNER_V9_BLOCKING_COVERAGE_EXTRA_ROWS");
    }
}
