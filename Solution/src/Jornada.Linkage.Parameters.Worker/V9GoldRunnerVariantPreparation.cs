using System.Security.Cryptography;
using System.Text.Json;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Prepare a reviewable artifact after conditional validation; never runs or enables V9.</summary>
public static class V9GoldRunnerVariantPreparation
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Uso: --v9-prepare-gold-runner <finalist-validation.json> <diretório-fits> <saída-nova>");
        var validationBytes = await File.ReadAllBytesAsync(args[1]);
        using var report = JsonDocument.Parse(validationBytes);
        var root = report.RootElement;
        var winner = root.GetProperty("bestConditionalSnapshotSha256").GetString();
        if (winner is not { Length: 64 } || winner.Any(c => !Uri.IsHexDigit(c))
            || root.GetProperty("refitted").GetBoolean() || root.GetProperty("testUsed").GetBoolean()
            || !root.GetProperty("measured").EnumerateArray().Any(x => x.GetProperty("SnapshotSha256").GetString() == winner))
            throw new InvalidDataException("Sem vencedor condicional congelado: variante Gold não pode ser preparada.");
        var parent = await V9FittedSnapshotArtifact.ReadPinnedAsync(Path.Combine(args[2], winner + ".fitted.json"), winner);
        var variant = parent.CreateGoldHarnessVariant();
        var output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) || File.Exists(output)) throw new InvalidOperationException("Variante exige saída nova.");
        Directory.CreateDirectory(output);
        await V9FittedSnapshotArtifact.WriteNewAsync(Path.Combine(output, variant.ContentSha256 + ".fitted.json"), variant);
        await using var reference = new FileStream(Path.Combine(output, "gold-runner-reference.json"), FileMode.CreateNew, FileAccess.Write);
        await JsonSerializer.SerializeAsync(reference, new {
            sourceSnapshotSha256 = parent.ContentSha256, variantSnapshotSha256 = variant.ContentSha256,
            sameFitFingerprintSha256 = variant.Payload.FitFingerprintSha256,
            finalistValidationSha256 = Convert.ToHexString(SHA256.HashData(validationBytes)).ToLowerInvariant(),
            grouping = "GOLD", refitted = false, measured = false, certified = false, activated = false,
            status = "PREPARED_ONLY_REAL_INCREMENTAL_RUNNER_AND_DT15_BINDING_STILL_REQUIRED" }, Json);
    }
}
