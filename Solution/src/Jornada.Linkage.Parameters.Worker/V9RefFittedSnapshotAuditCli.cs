using System.Data;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Read-only inspection of a *specific* SQL REF calibration record against a
/// separately pinned, portable V9 fitted snapshot. This does not make the SQL
/// calibration JSON equivalent to the portable fitted snapshot, does not load
/// an operational model and never changes a publication/DT-15 state.
/// </summary>
public static class V9RefFittedSnapshotAuditCli
{
    public sealed record ReferenceRow(
        string State, Guid? ModelId, string ReferenceAlgorithm,
        string StoredResultsSha256, string RecomputedResultsSha256,
        string? ModelAlgorithm, string? ModelStatus, string? ModelSnapshotReference);

    public sealed record Verdict(
        string Status, bool PublishedReference, bool SqlResultsHashVerified,
        bool LinkedModelAlgorithmVerified, bool ModelSnapshotShaMatches,
        bool OperationallyCertified = false, bool ActivationAllowed = false);

    /// <summary>
    /// The REF JSON's UTF-16 SQL hash differs from the canonical fitted
    /// snapshot's UTF-8 hash. Never compare those two unrelated digests.
    /// </summary>
    public static Verdict Assess(ReferenceRow row, string pinnedSnapshotSha256)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (pinnedSnapshotSha256 is not { Length: 64 }
            || pinnedSnapshotSha256.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("SHA-256 do snapshot fixado inválido.", nameof(pinnedSnapshotSha256));

        static bool SameSha(string? left, string right) =>
            left is { Length: 64 } && left.Equals(right, StringComparison.OrdinalIgnoreCase);

        var published = row.State == "PUBLICADA";
        var sqlHashVerified = SameSha(row.StoredResultsSha256, row.RecomputedResultsSha256);
        var modelAlgorithmVerified = row.ModelId.HasValue
            && row.ReferenceAlgorithm == LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion
            && row.ModelAlgorithm == row.ReferenceAlgorithm;
        var snapshotBound = modelAlgorithmVerified
            && SameSha(row.ModelSnapshotReference, pinnedSnapshotSha256);
        var status = !published ? "REF_NOT_PUBLISHED"
            : !sqlHashVerified ? "REF_RESULTS_DIGEST_MISMATCH"
            : !row.ModelId.HasValue ? "REF_NOT_LINKED_TO_MODEL"
            : !modelAlgorithmVerified ? "MODEL_ALGORITHM_MISMATCH"
            : !snapshotBound ? "PINNED_SNAPSHOT_NOT_BOUND_TO_MODEL"
            : "REFERENCE_AND_MODEL_METADATA_MATCH_NOT_CERTIFIED";

        return new(status, published, sqlHashVerified, modelAlgorithmVerified, snapshotBound);
    }

    public static async Task RunAsync(string[] args)
    {
        if (args is not { Length: 4 } || args[0] != "--v9-audit-ref-frozen-snapshot")
            throw new ArgumentException(
                "Uso: --v9-audit-ref-frozen-snapshot <SHA.fitted.json> <SHA-256> <codigo-ref-exato>.");

        var code = args[3];
        if (string.IsNullOrWhiteSpace(code) || code.Length > 120
            || code.Any(char.IsControl) || code != code.Trim())
            throw new ArgumentException("Código ref explícito inválido.", nameof(args));

        // Validate canonical JSON and SHA of the portable V9 fit BEFORE SQL access.
        var snapshot = await V9FittedSnapshotArtifact.ReadPinnedAsync(args[1], args[2]);
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
            ?? throw new InvalidOperationException("ConnectionStrings__Jornada explícita obrigatória.");
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog)
            || builder.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A auditoria REF exige database explícito diferente de master.");
        builder.ApplicationIntent = ApplicationIntent.ReadOnly;
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        // No SELECT * and no UPDATE/INSERT/EXEC. We verify SQL's actual byte-level
        // SHA of the REF JSON without returning the payload (potentially sensitive).
        await using var query = new SqlCommand("""
            SELECT r.estado, r.modelo_id, r.algoritmo_versao, r.resultados_sha256,
                   CONVERT(VARCHAR(64),
                       HASHBYTES('SHA2_256',CONVERT(VARBINARY(MAX),r.resultados_json)),2),
                   m.algoritmo_versao, m.status, m.snapshot_referencia
            FROM ref.calibracao_inicial_versao r
            LEFT JOIN identidade.modelo_linkage m ON m.modelo_id = r.modelo_id
            WHERE r.codigo = @codigo;
            """, connection);
        query.Parameters.Add("@codigo", SqlDbType.NVarChar, 120).Value = code;
        await using var reader = await query.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidDataException("Código ref não encontrado. Nenhum modelo presumido.");
        var row = new ReferenceRow(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7));
        if (await reader.ReadAsync())
            throw new InvalidDataException("Código ref não é unívoco.");
        var verdict = Assess(row, snapshot.ContentSha256);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "V9_REF_FROZEN_SNAPSHOT_READONLY_AUDIT_V1",
            referenceCode = code,
            database = builder.InitialCatalog,
            modelId = row.ModelId,
            snapshotSha256 = snapshot.ContentSha256,
            snapshotSchema = snapshot.Payload.SchemaVersion,
            blockingPassCount = snapshot.Payload.BlockingPasses.Count,
            activeTfUnits = snapshot.Payload.UnitTfCalibrations?.Count(x => x.State == V9SnapshotTfFitState.Active) ?? 0,
            frozenConfiguration = V9FittedSnapshotArtifact.DescribeFrozenConfiguration(snapshot),
            referenceResultsHashSha256 = row.StoredResultsSha256,
            verdict,
            limitations = "SQL REF result JSON is NOT the portable fit; matching metadata is not full binding, statistical certification or DT-15 activation."
        }));
        if (verdict.Status != "REFERENCE_AND_MODEL_METADATA_MATCH_NOT_CERTIFIED")
            Environment.ExitCode = 2; // read-only diagnostic fail-closed
    }
}
