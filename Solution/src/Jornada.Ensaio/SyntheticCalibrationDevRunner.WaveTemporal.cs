using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Ensaio;

public sealed partial class SyntheticCalibrationDevRunner
{
    private static async Task<SyntheticTemporalEvaluationEvidence> RunTemporalTruthEvaluationAsync(
        SyntheticCalibrationSettings settings, Guid modelId, IReadOnlyList<string> expectedSnapshotHashes,
        CancellationToken cancellationToken)
    {
        var output = Path.Combine(settings.RunDirectory, "synthetic-temporal-evaluation.json");
        var project = Path.Combine(settings.SolutionRoot,
            "src", "Jornada.Linkage.Evaluation", "Jornada.Linkage.Evaluation.csproj");
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ConnectionStrings__Jornada"] = settings.ConnectionString,
            ["Database__Provider"] = "SqlServer",
            ["DOTNET_ENVIRONMENT"] = RequiredEnvironment
        };
        await RunProcessAsync(settings.SolutionRoot, "dotnet",
            [
                "run", "--project", project, "--configuration", "Release", "--no-build", "--",
                "--synthetic-temporal-root", settings.GeneratedDirectory,
                "--model-id", modelId.ToString("D"),
                "--output", output,
                "--command-timeout-seconds",
                settings.EvaluationCommandTimeoutSeconds.ToString(CultureInfo.InvariantCulture)
            ], environment, cancellationToken);

        if (!File.Exists(output) || !File.Exists(output + ".sha256"))
            throw new InvalidOperationException("Avaliador temporal não gerou JSON e SHA-256.");
        await using var stream = File.OpenRead(output);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        var expected = (await File.ReadAllTextAsync(output + ".sha256", cancellationToken))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 do relatório temporal divergente.");

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(output, cancellationToken));
        var waves = doc.RootElement.GetProperty("waves");
        if (waves.GetArrayLength() != expectedSnapshotHashes.Count
            || doc.RootElement.GetProperty("modelPromotionAttempted").GetBoolean()
            || doc.RootElement.GetProperty("truthConsumedByIngestionOrCalibrator").GetBoolean())
            throw new InvalidDataException("Relatório temporal incompleto ou sem garantias DEV.");

        var metrics = new List<SyntheticTemporalWaveEvidence>();
        for (var index = 0; index < waves.GetArrayLength(); index++)
        {
            var entry = waves[index];
            var hash = entry.GetProperty("snapshotSha256").GetString();
            if (!string.Equals(hash, expectedSnapshotHashes[index], StringComparison.OrdinalIgnoreCase)
                || entry.GetProperty("wave").GetInt32() != index + 1
                || entry.GetProperty("measurementStatus").GetString() != "MEDIDO_SHADOW_REAL_SEM_PUBLICACAO")
                throw new InvalidDataException("Evidencia temporal de onda nao comprovada.");
            var precision = entry.GetProperty("precision");
            var recall = entry.GetProperty("recall");
            metrics.Add(new SyntheticTemporalWaveEvidence(index + 1,
                precision.ValueKind == JsonValueKind.Null ? null : precision.GetDecimal(),
                recall.ValueKind == JsonValueKind.Null ? null : recall.GetDecimal()));
        }
        Console.WriteLine($"SYNTHETIC TEMPORAL: ondas={waves.GetArrayLength()}; medidas shadow sem publicacao.");
        return new SyntheticTemporalEvaluationEvidence(actual, metrics);
    }

    private sealed record SyntheticTemporalEvaluationEvidence(
        string Sha256, IReadOnlyList<SyntheticTemporalWaveEvidence> Waves);

    private sealed record SyntheticTemporalWaveEvidence(
        int Wave, decimal? Precision, decimal? Recall);
}
