using System.Security.Cryptography;
using System.Text.Json;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Prepare a reviewable artifact after conditional validation; never runs or enables V9.</summary>
public static class V9GoldRunnerVariantPreparation
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    /// <summary>
    /// Validate the complete, still-uncertified conditional VALIDATION receipt
    /// before preparing a pinned Gold variant. The receipt is NOT an approval,
    /// REF publication, or independent verification of the measurements.
    /// </summary>
    public static string RequireConditionalWinner(JsonElement root)
    {
        static bool IsSha(string? value) => value is { Length: 64 }
            && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
        static bool False(JsonElement obj, string name) => obj.TryGetProperty(name, out var flag)
            && flag.ValueKind == JsonValueKind.False;
        static bool Sha(JsonElement obj, string name, out string? value)
        {
            value = obj.TryGetProperty(name, out var element)
                && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
            return IsSha(value);
        }

        if (root.ValueKind != JsonValueKind.Object
            || !Sha(root, "bestConditionalSnapshotSha256", out var winner)
            || !Sha(root, "selectionEnvelopeSha256", out _)
            || !False(root, "testUsed") || !False(root, "refitted")
            || !False(root, "certified") || !False(root, "activated")
            || !False(root, "identityClusterIidIndependentlyValidated")
            || !root.TryGetProperty("selectedApprovedSnapshotSha256", out var approved)
            || approved.ValueKind != JsonValueKind.Null
            || !root.TryGetProperty("status", out var status)
            || status.ValueKind != JsonValueKind.String
            || status.GetString() != "CONDITIONAL_ONLY_IID_NOT_INDEPENDENTLY_VALIDATED"
            || !root.TryGetProperty("comparedConfigurations", out var compared)
            || compared.ValueKind != JsonValueKind.Number
            || !compared.TryGetInt32(out var count) || count is < 1 or > 3
            || !root.TryGetProperty("measured", out var rows)
            || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != count)
            throw new InvalidDataException("Recibo V9 não é uma VALIDATION condicional congelada, não certificada.");

        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var winnerHasConditionalBound = false;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object
                || !Sha(row, "SnapshotSha256", out var candidate)
                || !hashes.Add(candidate!)
                || !False(row, "Refitted")
                || !row.TryGetProperty("Metric", out var metric)
                || metric.ValueKind != JsonValueKind.Object
                || !False(metric, "SyntheticGoldOracleGrouping")
                || !metric.TryGetProperty("BonferroniComparedConfigurations", out var multiplicity)
                || multiplicity.ValueKind != JsonValueKind.Number
                || !multiplicity.TryGetInt32(out var rowCount) || rowCount != count)
                throw new InvalidDataException("Finalistas V9 incompletos, repetidos ou não comparáveis.");

            if (candidate == winner
                && metric.TryGetProperty("ConditionalClusterCpFdrUpper", out var bound)
                && bound.ValueKind == JsonValueKind.Number
                && bound.TryGetDouble(out var rate)
                && double.IsFinite(rate) && rate is >= 0d and <= 1d)
                winnerHasConditionalBound = true;
        }
        if (!winnerHasConditionalBound)
            throw new InvalidDataException("Vencedor V9 sem medição condicional de risco rastreável.");
        return winner!;
    }

    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Uso: --v9-prepare-gold-runner <finalist-validation.json> <diretório-fits> <saída-nova>");
        var validationBytes = await File.ReadAllBytesAsync(args[1]);
        using var report = JsonDocument.Parse(validationBytes);
        var root = report.RootElement;
        var winner = RequireConditionalWinner(root);
        var parent = await V9FittedSnapshotArtifact.ReadPinnedAsync(Path.Combine(args[2], winner + ".fitted.json"), winner);
        var variant = parent.CreateGoldHarnessVariant();
        var output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) || File.Exists(output)) throw new InvalidOperationException("Variante exige saída nova.");
        Directory.CreateDirectory(output);
        await V9FittedSnapshotArtifact.WriteNewAsync(Path.Combine(output, variant.ContentSha256 + ".fitted.json"), variant);
        await using var reference = new FileStream(Path.Combine(output, "gold-runner-reference.json"), FileMode.CreateNew, FileAccess.Write);
        await JsonSerializer.SerializeAsync(reference, new {
            sourceSnapshotSha256 = parent.ContentSha256, variantSnapshotSha256 = variant.ContentSha256,
            selectionEnvelopeSha256 = root.GetProperty("selectionEnvelopeSha256").GetString(),
            sameFitFingerprintSha256 = variant.Payload.FitFingerprintSha256,
            finalistValidationSha256 = Convert.ToHexString(SHA256.HashData(validationBytes)).ToLowerInvariant(),
            grouping = "GOLD", refitted = false, measured = false, certified = false, activated = false,
            status = "PREPARED_ONLY_REAL_INCREMENTAL_RUNNER_AND_DT15_BINDING_STILL_REQUIRED" }, Json);
    }
}
