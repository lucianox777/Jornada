using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Executes both models with the real C# synthetic evaluator, without persistence
/// or changing the model status. The resident SQL Development marker is checked
/// before any synthetic sidecar is opened. Model hashes and statuses are checked
/// before and after both replays.
/// </summary>
public static class Dt15SyntheticPairCommand
{
    public static async Task<Dt15SyntheticPairedDossier> ExecuteAsync(
        SqlConnection connection, Guid activeModelId, Guid draftModelId,
        string corpusRoot, int maxCandidatePairs, int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (activeModelId == Guid.Empty || draftModelId == Guid.Empty
            || activeModelId == draftModelId)
            throw new ArgumentException("Forneça IDs distintos e válidos de ATIVO e RASCUNHO.");
        if (maxCandidatePairs is < 1_000 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(maxCandidatePairs));
        if (timeoutSeconds is < 10 or > 3_600)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));

        var baseline = await PreflightAsync(connection, activeModelId, draftModelId,
            timeoutSeconds, cancellationToken);
        var evaluator = new SyntheticEvaluationEngine(connection, timeoutSeconds);
        var root = Path.GetFullPath(corpusRoot);

        var activeReport = await evaluator.EvaluateAsync(new SyntheticEvaluationOptions(
            activeModelId, root, maxCandidatePairs, timeoutSeconds,
            AllowActiveForDt15Pair: true), cancellationToken);
        var draftReport = await evaluator.EvaluateAsync(new SyntheticEvaluationOptions(
            draftModelId, root, maxCandidatePairs, timeoutSeconds), cancellationToken);

        var current = await PreflightAsync(connection, activeModelId, draftModelId,
            timeoutSeconds, cancellationToken);
        if (baseline != current
            || !string.Equals(baseline.ActiveSnapshot, activeReport.Model.ModelSnapshotSha256,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(baseline.DraftSnapshot, draftReport.Model.ModelSnapshotSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "DT-15: o ATIVO, RASCUNHO ou seus fingerprints mudaram durante o replay; " +
                "evidência recusada.");

        return Dt15SyntheticPairedComparison.Compare(
            Dt15SyntheticPairedComparison.FromReport(activeReport, baseline.ActivePartition),
            Dt15SyntheticPairedComparison.FromReport(draftReport, baseline.DraftPartition));
    }

    private static async Task<Preflight> PreflightAsync(
        SqlConnection connection, Guid activeId, Guid draftId,
        int timeoutSeconds, CancellationToken cancellationToken)
    {
        await using (var guard = new SqlCommand("""
            SELECT CONVERT(NVARCHAR(32),(
                SELECT value FROM sys.extended_properties
                WHERE class=0 AND name=N'Jornada.EnvironmentProfile'));
            """, connection) { CommandTimeout = timeoutSeconds })
        {
            var profile = await guard.ExecuteScalarAsync(cancellationToken) as string;
            if (!string.Equals(profile, "Development", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "DT-15 synthetic FS replay requires resident SQL Development profile.");
        }

        var activeCount = await ScalarAsync<long>(connection,
            "SELECT COUNT_BIG(*) FROM identidade.modelo_linkage WHERE status=N'ATIVO';",
            timeoutSeconds, cancellationToken);
        if (activeCount != 1)
            throw new InvalidOperationException(
                "DT-15: o replay exige exatamente um modelo ATIVO.");
        var active = await ReadModelAsync(connection, activeId, timeoutSeconds, cancellationToken);
        var draft = await ReadModelAsync(connection, draftId, timeoutSeconds, cancellationToken);
        if (active.Status != "ATIVO" || draft.Status != "RASCUNHO")
            throw new InvalidOperationException(
                "DT-15: o modelo ATIVO-base e o RASCUNHO devem manter seus estados.");
        if (active.Partition != draft.Partition)
            throw new InvalidOperationException(
                "DT-15: modelos usam seed ou percentuais VALIDATION/TEST diferentes; " +
                "não existe comparação pareada honesta.");

        return new Preflight(active.Snapshot, draft.Snapshot,
            active.Partition, draft.Partition);
    }

    private static async Task<ModelPreflight> ReadModelAsync(
        SqlConnection connection, Guid id, int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT m.status,
                MAX(CASE WHEN p.nome=N'FS_DECISION_CALIBRATION_SEED' THEN p.valor END),
                MAX(CASE WHEN p.nome=N'FS_DECISION_CALIBRATION_VALIDATION_BP' THEN p.valor END),
                MAX(CASE WHEN p.nome=N'FS_DECISION_CALIBRATION_TEST_BP' THEN p.valor END)
            FROM identidade.modelo_linkage m
            LEFT JOIN identidade.parametro_linkage p ON p.modelo_id=m.modelo_id
            WHERE m.modelo_id=@model_id
            GROUP BY m.status;
            """, connection) { CommandTimeout = timeoutSeconds };
        command.Parameters.Add("@model_id", SqlDbType.UniqueIdentifier).Value = id;
        string status;
        Dt15PartitionContract partition;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)
                || reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3))
                throw new InvalidDataException(
                    "Modelo ausente ou sem contrato de partição FS de decisão.");
            status = reader.GetString(0);
            partition = new Dt15PartitionContract(
                ExactInt(reader.GetDecimal(1)),
                ExactInt(reader.GetDecimal(2)),
                ExactInt(reader.GetDecimal(3)));
            if (await reader.ReadAsync(cancellationToken))
                throw new InvalidDataException("Duplicidade na leitura do modelo DT-15.");
        }

        await using var snapshot = new SqlCommand("""
            DECLARE @hash BINARY(32);
            EXEC auditoria.sp_calcular_fingerprint_modelo_linkage
                @modelo_id=@model_id, @fingerprint=@hash OUTPUT;
            SELECT @hash;
            """, connection) { CommandTimeout = timeoutSeconds };
        snapshot.Parameters.Add("@model_id", SqlDbType.UniqueIdentifier).Value = id;
        var data = await snapshot.ExecuteScalarAsync(cancellationToken);
        if (data is not byte[] hash || hash.Length != 32)
            throw new InvalidDataException("Fingerprint do modelo DT-15 inválido.");
        return new ModelPreflight(status,
            Convert.ToHexString(hash).ToLowerInvariant(), partition);
    }

    private static int ExactInt(decimal value)
    {
        if (value != decimal.Truncate(value) || value < 0 || value > int.MaxValue)
            throw new InvalidDataException("Parâmetro de partição FS não inteiro.");
        return (int)value;
    }

    private static async Task<T> ScalarAsync<T>(
        SqlConnection connection, string query, int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(query, connection)
        {
            CommandTimeout = timeoutSeconds
        };
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not T result)
            throw new InvalidDataException("Preflight SQL DT-15 retornou tipo inesperado.");
        return result;
    }

    private sealed record ModelPreflight(
        string Status, string Snapshot, Dt15PartitionContract Partition);
    private sealed record Preflight(
        string ActiveSnapshot, string DraftSnapshot,
        Dt15PartitionContract ActivePartition,
        Dt15PartitionContract DraftPartition);
}
