using System.Data;
using System.Globalization;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Operational.Sql;

/// <summary>
/// Isolated opt-in Processor-persistence seam for the frozen V9 projection.
/// Shares the real legacy Gold/history snapshot reader and alias-merging
/// semantics but never uses the legacy V2 default fingerprint, never refreshes
/// an existing index, and never activates V9 in production. A future migration
/// and explicit PO approval are required for production enablement.
/// </summary>
public static partial class BlockingProjectionPersistence
{
    public const int V9PinnedMaximumNewReferenceKeys = 2048;

    /// <summary>
    /// Test-only atomic creation of the first V9 index for a new, complete,
    /// CPF-less Gold reference. Caller owns the transaction and must commit
    /// or roll it back. No model activation, publishing or TEST opening.
    /// </summary>
    public static async Task<int> MaterializeNewPinnedV9SqlServerAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid pessoaUuid, int maximumPrefixLength,
        string expectedProjectionFingerprintSha256, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (connection.State != ConnectionState.Open || transaction.Connection != connection)
            throw new InvalidOperationException(
                "Persistência pinada V9 exige conexão e transação SQL válidas.");
        if (pessoaUuid == Guid.Empty)
            throw new ArgumentException("Referência Gold V9 obrigatória.", nameof(pessoaUuid));

        V9CalibrationBlockingProjection.ValidateMaximumPrefixLength(maximumPrefixLength);
        var expected = V9CalibrationBlockingProjection.ComputeSchemaFingerprint(maximumPrefixLength);
        if (string.IsNullOrWhiteSpace(expectedProjectionFingerprintSha256)
            || !string.Equals(expected, expectedProjectionFingerprintSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Materialização V9 recusada: SHA da projeção diverge do plano congelado.");

        await using (var db = new SqlCommand("SELECT DB_NAME();", connection, transaction))
            if (!string.Equals((string?)await db.ExecuteScalarAsync(ct),
                    "JornadaHarness", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Caminho Processor V9 opt-in proibido fora do SQL descartável JornadaHarness.");

        // Fail before reading names/history or mutating any SQL. The existing
        // LEGACY Refresh* methods are unaffected: this opt-in requires an
        // UNINDEXED, CPF-less, canonical Gold reference, not an already
        // materialized key to be silently replaced.
        await using (var admission = new SqlCommand("""
            SELECT CASE WHEN EXISTS (
                SELECT 1
                  FROM gold.pessoa g WITH(UPDLOCK,HOLDLOCK)
                  JOIN identidade.pessoa p WITH(HOLDLOCK)
                    ON p.pessoa_uuid=g.pessoa_uuid
                 WHERE g.pessoa_uuid=@id AND p.status=N'ATIVO'
                   AND g.estado_identidade=N'REFERENCIA'
                   AND g.completude_nucleo=N'COMPLETO'
                   AND g.status_cpf=N'SEM_CPF' AND g.cpf IS NULL
                   AND g.nome_completo IS NOT NULL
                   AND g.nome_mae IS NOT NULL AND g.data_nascimento IS NOT NULL
            ) AND NOT EXISTS (
                SELECT 1 FROM identidade.blocking_chave WITH(UPDLOCK,HOLDLOCK)
                 WHERE pessoa_uuid=@id
            ) THEN 1 ELSE 0 END;
            """, connection, transaction))
        {
            admission.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = pessoaUuid;
            if (Convert.ToInt32(await admission.ExecuteScalarAsync(ct),
                    CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException(
                    "Referência Gold V9 ausente/inválida ou possui materialização anterior.");
        }

        // The same private loader used by the actual Processor refresh:
        // current Gold, verified Silver linked name aliases and versioned
        // metadata. Only V9-declared features can reach V9 blocking_chave.
        var snapshot = await LoadSqlServerSnapshotAsync(
            connection, transaction, pessoaUuid, ct, maximumPrefixLength);
        if (snapshot.Rows.Count is < 1 or > V9PinnedMaximumNewReferenceKeys)
            throw new InvalidDataException(
                "Snapshot Gold/alias V9 incompleto ou excede teto: não truncar.");

        foreach (var row in snapshot.Rows)
        {
            if (string.IsNullOrWhiteSpace(row.Feature) || row.Feature.Length > 80
                || string.IsNullOrWhiteSpace(row.Value) || row.Value.Length > 500
                || row.TemporalSemantics is not ("VERSIONED_ALIAS" or "STABLE_IDENTITY_DATUM")
                || (row.ValidTo is { } end && end < row.ValidFrom))
                throw new InvalidDataException("Feature ou intervalo temporal V9 inválido.");
            // The complete feature catalog is frozen into the projection SHA,
            // including prefix lengths. Do not leak transversal attributes.
            if (!V9CalibrationBlockingProjection.SupportedFeatures(maximumPrefixLength)
                .Contains(row.Feature, StringComparer.Ordinal))
                throw new InvalidDataException("Feature não declarada na projeção V9 pinada.");

            await using var insert = new SqlCommand("""
                INSERT identidade.blocking_chave(
                    pessoa_uuid,normalizacao_versao,atributo,valor_normalizado,
                    semantica_temporal,vigencia_inicio,vigencia_fim,
                    projection_schema_version,projection_fingerprint_sha256)
                VALUES(
                    @id,@normalization,@feature,@value,
                    @temporal,@from,@to,@schema,@fingerprint);
                """, connection, transaction);
            insert.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = pessoaUuid;
            insert.Parameters.Add("@normalization", SqlDbType.NVarChar, 80).Value =
                IdentityComparison.NormalizationVersion;
            insert.Parameters.Add("@feature", SqlDbType.NVarChar, 80).Value = row.Feature;
            insert.Parameters.Add("@value", SqlDbType.NVarChar, 500).Value = row.Value;
            insert.Parameters.Add("@temporal", SqlDbType.NVarChar, 30).Value =
                row.TemporalSemantics;
            insert.Parameters.Add("@from", SqlDbType.DateTimeOffset).Value = row.ValidFrom;
            insert.Parameters.Add("@to", SqlDbType.DateTimeOffset).Value =
                (object?)row.ValidTo ?? DBNull.Value;
            insert.Parameters.Add("@schema", SqlDbType.NVarChar, 120).Value =
                V9CalibrationBlockingProjection.SchemaVersion;
            insert.Parameters.Add("@fingerprint", SqlDbType.Char, 64).Value =
                expected.ToLowerInvariant();
            if (await insert.ExecuteNonQueryAsync(ct) != 1)
                throw new InvalidDataException("Materialização SQL V9 incompleta.");
        }
        return snapshot.Rows.Count;
    }
}
