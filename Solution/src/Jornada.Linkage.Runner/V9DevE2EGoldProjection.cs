using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// One-use V9 projection into the physical blocking_chave of an authenticated
/// disposable DEV E2E attempt. Reads EXISTING governed Gold REFERENCES, never
/// synthetic truth, never refits model evidence or changes Gold/Serving rows.
/// Existing legacy keys are preserved. Not wired to Processor or production.
/// </summary>
internal static class V9DevE2EGoldProjection
{
    internal sealed record Receipt(long GoldReferences, long Keys,
        string ProjectionFingerprintSha256, string CanonicalKeysSha256);

    internal static async Task<Receipt> MaterializeOnceAsync(
        SqlConnection connection, V9FittedSnapshot winner, Guid attempt,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(winner);
        if (attempt == Guid.Empty || connection.State != ConnectionState.Open)
            throw new InvalidOperationException("RUNNER_V9_DEV_E2E_INVALID_SQL_PROJECTION_CONTEXT");
        await V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(
            connection, attempt, ct);
        var runtime = V9FittedSnapshotExecutionRuntime.Load(
            winner.SerializeUtf8(), winner.ContentSha256);
        var prefix = runtime.MaximumPrefixLength
            ?? throw new InvalidDataException("RUNNER_V9_DEV_E2E_PROJECTION_NOT_FROZEN");
        if (prefix < 1 || prefix > V9CalibrationBlockingProjection.MaximumSupportedPrefixLength
            || runtime.ProjectionSchemaVersion != V9CalibrationBlockingProjection.SchemaVersion
            || runtime.ProjectionFingerprintSha256 !=
                V9CalibrationBlockingProjection.ComputeSchemaFingerprint(prefix))
            throw new InvalidDataException("RUNNER_V9_DEV_E2E_PROJECTION_SHA_MISMATCH");

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        // Never mix a second V9 projection with a preexisting/stale V9 index.
        // Retain legacy keys untouched, but refuse to silently coexist with
        // an already staged V9 index. One attempt, one winner, one projection.
        await using (var old = new SqlCommand("""
            SELECT COUNT_BIG(*) FROM identidade.blocking_chave WITH(UPDLOCK,HOLDLOCK)
             WHERE projection_schema_version=@schema;
            """, connection, tx))
        {
            old.Parameters.Add("@schema", SqlDbType.NVarChar, 120).Value =
                runtime.ProjectionSchemaVersion;
            if (Convert.ToInt64(await old.ExecuteScalarAsync(ct),
                CultureInfo.InvariantCulture) != 0)
                throw new InvalidDataException("RUNNER_V9_DEV_E2E_GOLD_V9_INDEX_NOT_FRESH");
        }

        var keys = new DataTable { Locale = CultureInfo.InvariantCulture };
        keys.Columns.Add("pessoa_uuid", typeof(Guid));
        keys.Columns.Add("normalizacao_versao", typeof(string));
        keys.Columns.Add("atributo", typeof(string));
        keys.Columns.Add("valor_normalizado", typeof(string));
        keys.Columns.Add("semantica_temporal", typeof(string));
        keys.Columns.Add("vigencia_inicio", typeof(DateTimeOffset));
        keys.Columns.Add("vigencia_fim", typeof(DateTimeOffset));
        keys.Columns.Add("projection_schema_version", typeof(string));
        keys.Columns.Add("projection_fingerprint_sha256", typeof(string));
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long goldCount = 0;
        // Freeze one contiguous index-as-of instant, and reject duplications
        // or incomplete Gold, instead of converting them into no-candidate.
        var asOf = DateTimeOffset.UtcNow;
        await using (var query = new SqlCommand("""
            SELECT g.pessoa_uuid,g.nome_completo,g.nome_mae,g.data_nascimento,
                   g.cpf,g.status_cpf,g.completude_nucleo,p.status
              FROM gold.pessoa g WITH(UPDLOCK,HOLDLOCK)
              JOIN identidade.pessoa p WITH(HOLDLOCK)
                ON p.pessoa_uuid=g.pessoa_uuid
             WHERE g.estado_identidade=N'REFERENCIA'
               AND g.cpf IS NULL
             ORDER BY g.pessoa_uuid;
            """, connection, tx) { CommandTimeout = 180 })
        {
            await using var rd = await query.ExecuteReaderAsync(ct);
            var seen = new HashSet<Guid>();
            while (await rd.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                var id = rd.GetGuid(0);
                // SQL Server uniqueidentifier ORDER BY is not .NET Guid
                // CompareTo ordering; duplicate detection is set-based.
                if (!seen.Add(id))
                    throw new InvalidDataException("RUNNER_V9_DEV_E2E_DUPLICATED_GOLD_REFERENCE");
                if (rd.IsDBNull(1) || rd.IsDBNull(2) || rd.IsDBNull(3)
                    || !rd.IsDBNull(4) || rd.GetString(5) != "SEM_CPF"
                    || rd.GetString(6) != "COMPLETO" || rd.GetString(7) != "ATIVO")
                    throw new InvalidDataException(
                        "RUNNER_V9_DEV_E2E_GOLD_REFERENCE_INCOMPLETE_OR_INVALID");
                var name = rd.GetString(1);
                var mother = rd.GetString(2);
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(mother))
                    throw new InvalidDataException("RUNNER_V9_DEV_E2E_GOLD_NAMES_MISSING");
                var projected = V9CalibrationBlockingProjection.Project(
                    name, mother, DateOnly.FromDateTime(rd.GetDateTime(3)), prefix)
                    .Distinct().OrderBy(x => x.Feature, StringComparer.Ordinal)
                    .ThenBy(x => x.Value, StringComparer.Ordinal).ToArray();
                if (projected.Length == 0)
                    throw new InvalidDataException("RUNNER_V9_DEV_E2E_GOLD_BLOCKING_EMPTY");
                foreach (var key in projected)
                {
                    if (key.Feature.Length is < 1 or > 80
                        || key.Value.Length is < 1 or > 500)
                        throw new InvalidDataException("RUNNER_V9_DEV_E2E_BLOCKING_KEY_INVALID");
                    var isAlias = key.Feature.StartsWith("name_prefix_", StringComparison.Ordinal)
                        || key.Feature.StartsWith("mother_name_prefix_", StringComparison.Ordinal)
                        || BlockingFeatureTemporalCatalog.Get(key.Feature)
                            == BlockingFeatureTemporalSemantics.VersionedAlias;
                    keys.Rows.Add(id, IdentityComparison.NormalizationVersion,
                        key.Feature, key.Value,
                        isAlias ? "VERSIONED_ALIAS" : "STABLE_IDENTITY_DATUM",
                        asOf, DBNull.Value, runtime.ProjectionSchemaVersion,
                        runtime.ProjectionFingerprintSha256);
                    sha.AppendData(Encoding.UTF8.GetBytes(
                        FormattableString.Invariant($"{id:D}|{key.Feature}|{key.Value}\n")));
                }
                goldCount++;
            }
        }

        if (goldCount > 0 && keys.Rows.Count == 0)
            throw new InvalidDataException("RUNNER_V9_DEV_E2E_BLOCKING_NOT_MATERIALIZED");
        if (keys.Rows.Count > 0)
        {
            using var bulk = new SqlBulkCopy(
                connection, SqlBulkCopyOptions.CheckConstraints, tx)
            {
                DestinationTableName = "identidade.blocking_chave",
                BatchSize = 2048,
                BulkCopyTimeout = 180
            };
            foreach (DataColumn col in keys.Columns)
                bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            await bulk.WriteToServerAsync(keys, ct);
        }

        // Require exact physical count AFTER all inserts, same transaction.
        await using (var verified = new SqlCommand("""
            SELECT COUNT_BIG(*),COUNT_BIG(DISTINCT pessoa_uuid)
              FROM identidade.blocking_chave WITH(HOLDLOCK)
             WHERE projection_schema_version=@schema
               AND projection_fingerprint_sha256=@sha;
            """, connection, tx))
        {
            verified.Parameters.Add("@schema", SqlDbType.NVarChar, 120).Value =
                runtime.ProjectionSchemaVersion;
            verified.Parameters.Add("@sha", SqlDbType.Char, 64).Value =
                runtime.ProjectionFingerprintSha256;
            await using var rd = await verified.ExecuteReaderAsync(ct);
            if (!await rd.ReadAsync(ct)
                || rd.GetInt64(0) != keys.Rows.Count
                || rd.GetInt64(1) != goldCount
                || await rd.ReadAsync(ct))
                throw new InvalidDataException("RUNNER_V9_DEV_E2E_GOLD_INDEX_COUNT_MISMATCH");
        }
        await tx.CommitAsync(ct);
        return new Receipt(goldCount, keys.Rows.Count, runtime.ProjectionFingerprintSha256,
            Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant());
    }
}
