using System.Text.Json;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Bounded REF calibration metadata inventory, exclusively SELECT. No choice
/// of winner, no active-model fallback, no refit, publication, or activation.
/// Requires SQL credentials restricted to SELECT; ApplicationIntent alone
/// does not enforce database permissions.
/// </summary>
public static class V9RefInventoryCli
{
    public sealed record Metadata(
        string State, string Origin, string ReferenceAlgorithm, Guid? ModelId,
        string StoredSqlResultsSha256, string RecomputedSqlResultsSha256,
        string? ModelAlgorithm, string? ModelSnapshotReference);

    private static bool IsSha256(string? sha) => sha is { Length: 64 }
        && sha.All(Uri.IsHexDigit);

    public static string Assess(Metadata item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Origin != "SINTETICA") return "UNEXPECTED_REFERENCE_ORIGIN";
        if (item.State != "PUBLICADA") return "REF_NOT_PUBLISHED";
        if (!IsSha256(item.StoredSqlResultsSha256)
            || !IsSha256(item.RecomputedSqlResultsSha256)
            || !item.StoredSqlResultsSha256.Equals(item.RecomputedSqlResultsSha256,
                StringComparison.OrdinalIgnoreCase))
            return "REF_RESULTS_DIGEST_MISMATCH";
        if (item.ReferenceAlgorithm != LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion)
            return "NOT_V9_ALGORITHM";
        if (item.ModelId is null) return "REF_NOT_LINKED_TO_MODEL";
        if (item.ModelAlgorithm != item.ReferenceAlgorithm) return "MODEL_ALGORITHM_MISMATCH";
        if (!IsSha256(item.ModelSnapshotReference)) return "NO_DIRECT_PINNED_FIT_SHA_IN_MODEL";
        return "V9_METADATA_PRESENT_FIT_SHA_NOT_YET_VERIFIED";
    }

    public static async Task RunAsync(string[] args)
    {
        if (args is not { Length: 1 } || args[0] != "--v9-audit-ref-inventory")
            throw new ArgumentException("Uso: --v9-audit-ref-inventory.");

        var text = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
            ?? throw new InvalidOperationException("ConnectionStrings__Jornada explícita obrigatória.");
        var builder = new SqlConnectionStringBuilder(text);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog)
            || builder.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A auditoria REF exige database explícito diferente de master.");
        builder.ApplicationIntent = ApplicationIntent.ReadOnly;
        await using var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("""
            SELECT TOP (50) r.codigo, r.estado, r.origem, r.modelo_id,
                   r.algoritmo_versao, r.resultados_sha256,
                   CONVERT(VARCHAR(64),
                       HASHBYTES('SHA2_256',CONVERT(VARBINARY(MAX),r.resultados_json)),2),
                   r.corpus_sha256, m.algoritmo_versao, m.status,
                   m.snapshot_referencia
            FROM ref.calibracao_inicial_versao r
            LEFT JOIN identidade.modelo_linkage m ON m.modelo_id = r.modelo_id
            ORDER BY r.calibracao_versao_id DESC;
            """, conn);
        var results = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Metadata(
                reader.GetString(1), reader.GetString(2), reader.GetString(4),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetString(5), reader.GetString(6),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(10) ? null : reader.GetString(10));
            results.Add(new
            {
                code = reader.GetString(0),
                origin = row.Origin,
                state = row.State,
                modelId = row.ModelId,
                modelStatus = reader.IsDBNull(9) ? null : reader.GetString(9),
                referenceAlgorithm = row.ReferenceAlgorithm,
                corpusSha256 = reader.GetString(7),
                referenceResultsSqlSha256 = row.StoredSqlResultsSha256,
                sqlResultsHashVerified = IsSha256(row.StoredSqlResultsSha256)
                    && row.StoredSqlResultsSha256.Equals(row.RecomputedSqlResultsSha256,
                        StringComparison.OrdinalIgnoreCase),
                // A direct SHA is informative, NOT proof of loading or certification.
                declaredModelFitSha256 = IsSha256(row.ModelSnapshotReference)
                    ? row.ModelSnapshotReference : null,
                status = Assess(row),
                operationallyCertified = false,
                activationAllowed = false
            });
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "V9_REF_CALIBRATION_INVENTORY_READONLY_V1",
            database = builder.InitialCatalog,
            maxRecords = 50,
            records = results,
            operationallyCertified = false,
            activationAllowed = false,
            limitations = "Read-only metadata discovery. REF SQL JSON and portable UTF-8 fit have distinct digests. " +
                "Use --v9-audit-ref-frozen-snapshot with exact code, external SHA and canonical snapshot bytes. " +
                "ApplicationIntent=ReadOnly is not a replacement for SELECT-only SQL credentials."
        }));
        if (results.Count == 0) Environment.ExitCode = 2;
    }
}
