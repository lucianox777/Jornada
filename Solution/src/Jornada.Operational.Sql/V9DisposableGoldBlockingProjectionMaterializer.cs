using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Operational.Sql;

/// <summary>
/// Opt-in, disposable-only physical V9 projection materializer.
/// Reads actual persisted gold.pessoa references, uses the production Contracts
/// V9 projector and persists to the actual identidade.blocking_chave table.
/// It is NOT wired into Processor publication, production DI or scientific TEST.
/// Does not change the legacy BlockingProjectionPersistence.Refresh* behavior.
/// </summary>
public static class V9DisposableGoldBlockingProjectionMaterializer
{
    public const int MaximumGoldReferences = 512;
    public const int MaximumProjectionRows = 16_384;

    public sealed record Receipt(
        int ReadGoldReferences, int MaterializedBlockingRows,
        string ProjectionSchemaVersion, string ProjectionFingerprintSha256,
        string CanonicalKeysSha256, bool ProductionGoldPublicationCertified = false);

    public static async Task<Receipt> MaterializeAsync(
        SqlConnection connection,
        IReadOnlyList<Guid> exactDiagnosticGoldUuids,
        int maximumPrefixLength,
        string expectedProjectionFingerprintSha256,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(exactDiagnosticGoldUuids);
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Materializador V9 exige conexão SQL aberta.");
        V9CalibrationBlockingProjection.ValidateMaximumPrefixLength(maximumPrefixLength);
        var fingerprint = V9CalibrationBlockingProjection.ComputeSchemaFingerprint(maximumPrefixLength);
        if (expectedProjectionFingerprintSha256 is null
            || !string.Equals(expectedProjectionFingerprintSha256, fingerprint,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Fingerprint de projeção Gold V9 não corresponde ao plano pinado.");

        if (exactDiagnosticGoldUuids.Count is < 2 or > MaximumGoldReferences
            || exactDiagnosticGoldUuids.Any(id => id == Guid.Empty)
            || exactDiagnosticGoldUuids.Distinct().Count() != exactDiagnosticGoldUuids.Count)
            throw new InvalidDataException("Prova Gold V9 requer referências únicas, íntegras e limitadas.");

        await using (var database = new SqlCommand("SELECT DB_NAME();", connection))
            if (!string.Equals((string?)await database.ExecuteScalarAsync(ct),
                    "JornadaHarness", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Materialização Gold V9 permitida SOMENTE no JornadaHarness descartável.");

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        // A single-use empty physical table is mandatory; this test-only
        // routine never deletes/migrates LEGACY keys and cannot silently take
        // over a Gold/Serving index built by another processing mode.
        await using (var admission = new SqlCommand("""
            SELECT CASE WHEN NOT EXISTS
                  (SELECT 1 FROM identidade.blocking_chave WITH(UPDLOCK,HOLDLOCK))
              AND (SELECT COUNT_BIG(*) FROM gold.pessoa WITH(HOLDLOCK)) = @expected
              AND (SELECT COUNT_BIG(*) FROM identidade.pessoa WITH(HOLDLOCK)) = @expected
              THEN 1 ELSE 0 END;
            """, connection, transaction))
        {
            admission.Parameters.Add("@expected", SqlDbType.BigInt).Value = exactDiagnosticGoldUuids.Count;
            if (Convert.ToInt32(await admission.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException(
                    "Materialização Gold TEST requer banco exclusivamente diagnóstico sem chaves prévias.");
        }

        await using (var setup = new SqlCommand("""
            CREATE TABLE #v9_gold_source_ids(
                pessoa_uuid uniqueidentifier NOT NULL PRIMARY KEY);
            CREATE TABLE #v9_gold_projected_keys(
                pessoa_uuid uniqueidentifier NOT NULL,
                normalizacao_versao nvarchar(80) NOT NULL,
                atributo nvarchar(80) NOT NULL,
                valor_normalizado nvarchar(500) NOT NULL,
                semantica_temporal nvarchar(30) NOT NULL,
                vigencia_inicio datetimeoffset(7) NOT NULL,
                projection_schema_version nvarchar(120) NOT NULL,
                projection_fingerprint_sha256 char(64) NOT NULL);
            """, connection, transaction))
            await setup.ExecuteNonQueryAsync(ct);

        var ids = new DataTable { Locale = CultureInfo.InvariantCulture };
        ids.Columns.Add("pessoa_uuid", typeof(Guid));
        foreach (var id in exactDiagnosticGoldUuids)
            ids.Rows.Add(id);
        using (var bulkIds = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction)
        {
            DestinationTableName = "#v9_gold_source_ids",
            BatchSize = MaximumGoldReferences,
            BulkCopyTimeout = 60
        })
        {
            bulkIds.ColumnMappings.Add("pessoa_uuid", "pessoa_uuid");
            await bulkIds.WriteToServerAsync(ids, ct);
        }

        var keys = new DataTable { Locale = CultureInfo.InvariantCulture };
        keys.Columns.Add("pessoa_uuid", typeof(Guid));
        keys.Columns.Add("normalizacao_versao", typeof(string));
        keys.Columns.Add("atributo", typeof(string));
        keys.Columns.Add("valor_normalizado", typeof(string));
        keys.Columns.Add("semantica_temporal", typeof(string));
        keys.Columns.Add("vigencia_inicio", typeof(DateTimeOffset));
        keys.Columns.Add("projection_schema_version", typeof(string));
        keys.Columns.Add("projection_fingerprint_sha256", typeof(string));
        var goldCount = 0;
        var observed = new HashSet<Guid>();
        var canonical = new List<string>();

        await using (var read = new SqlCommand("""
            SELECT g.pessoa_uuid,g.nome_completo,g.nome_mae,g.data_nascimento,
                   g.atualizado_em,g.estado_identidade,g.status_cpf,g.cpf,g.completude_nucleo
              FROM #v9_gold_source_ids s
              JOIN gold.pessoa g ON g.pessoa_uuid=s.pessoa_uuid
             ORDER BY g.pessoa_uuid;
            """, connection, transaction) { CommandTimeout = 120 })
        {
            await using var reader = await read.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                var uuid = reader.GetGuid(0);
                if (!observed.Add(uuid)
                    || reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3)
                    || reader.GetString(5) != "REFERENCIA"
                    || reader.GetString(6) != "SEM_CPF" || !reader.IsDBNull(7)
                    || reader.GetString(8) != "COMPLETO")
                    throw new InvalidDataException(
                        "Referência Gold V9 ausente, duplicada, incompleta ou com CPF.");

                var projected = V9CalibrationBlockingProjection.Project(
                    reader.GetString(1), reader.GetString(2),
                    DateOnly.FromDateTime(reader.GetDateTime(3)), maximumPrefixLength);
                if (projected.Count == 0)
                    throw new InvalidDataException("Gold V9 não produziu chaves de blocking.");
                var asOf = reader.GetDateTimeOffset(4);
                foreach (var key in projected.Distinct())
                {
                    if (keys.Rows.Count >= MaximumProjectionRows
                        || key.Feature.Length is < 1 or > 80
                        || key.Value.Length is < 1 or > 500)
                        throw new InvalidDataException("Projeção V9 Gold ultrapassou o limite declarado.");
                    var alias = key.Feature.StartsWith("name_prefix_", StringComparison.Ordinal)
                        || key.Feature.StartsWith("mother_name_prefix_", StringComparison.Ordinal)
                        || BlockingFeatureTemporalCatalog.Get(key.Feature) ==
                            BlockingFeatureTemporalSemantics.VersionedAlias;
                    keys.Rows.Add(uuid, IdentityComparison.NormalizationVersion,
                        key.Feature, key.Value,
                        alias ? "VERSIONED_ALIAS" : "STABLE_IDENTITY_DATUM", asOf,
                        V9CalibrationBlockingProjection.SchemaVersion, fingerprint);
                    canonical.Add(FormattableString.Invariant(
                        $"{uuid:D}|{key.Feature}|{key.Value}|{(alias ? "ALIAS" : "STABLE")}\n"));
                }
                goldCount++;
            }
        }

        if (goldCount != exactDiagnosticGoldUuids.Count
            || observed.Count != exactDiagnosticGoldUuids.Count || keys.Rows.Count == 0)
            throw new InvalidDataException("Materializador V9 não leu todas as referências Gold.");

        using (var bulkKeys = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction)
        {
            DestinationTableName = "#v9_gold_projected_keys",
            BatchSize = 2048,
            BulkCopyTimeout = 120
        })
        {
            foreach (DataColumn column in keys.Columns)
                bulkKeys.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            await bulkKeys.WriteToServerAsync(keys, ct);
        }
        await using (var publish = new SqlCommand("""
            INSERT identidade.blocking_chave(
                pessoa_uuid,normalizacao_versao,atributo,valor_normalizado,
                semantica_temporal,vigencia_inicio,vigencia_fim,
                projection_schema_version,projection_fingerprint_sha256)
            SELECT pessoa_uuid,normalizacao_versao,atributo,valor_normalizado,
                   semantica_temporal,vigencia_inicio,NULL,
                   projection_schema_version,projection_fingerprint_sha256
              FROM #v9_gold_projected_keys;
            """, connection, transaction) { CommandTimeout = 120 })
        {
            var inserted = await publish.ExecuteNonQueryAsync(ct);
            if (inserted != keys.Rows.Count)
                throw new InvalidDataException(
                    "Materialização operacional de chaves Gold V9 não foi completa.");
        }

        await transaction.CommitAsync(ct);
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Concat(canonical.OrderBy(x => x, StringComparer.Ordinal))))).ToLowerInvariant();
        return new(goldCount, keys.Rows.Count,
            V9CalibrationBlockingProjection.SchemaVersion, fingerprint, sha,
            ProductionGoldPublicationCertified: false);
    }
}
