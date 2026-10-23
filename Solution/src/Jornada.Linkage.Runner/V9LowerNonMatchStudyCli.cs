using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Explicit, manual TRAIN_SELECTION-only physical Gold Pareto study. Reuses
/// the single V9 calibrator; it does not mint identities in the lower band,
/// touch TEST, or change a database beyond the guarded disposable harness.
/// </summary>
public static class V9LowerNonMatchStudyCli
{
    public const string Flag = "--v9-lower-nonmatch-study";
    public sealed record DeclaredGrid(
        IReadOnlyList<decimal> CandidateLowerThresholds,
        int MinimumIndependentIdentityGroups,
        int MinimumAlreadyRepresentedSources,
        bool ManualCalibrationRequested,
        bool TestUsed);

    private static readonly JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public static async Task<V9LowerNonMatchTrainCalibrator.Study> RunAsync(string[] args)
    {
        if (args is null || args.Length != 7 || args[0] != Flag)
            throw new ArgumentException(
                "Uso: --v9-lower-nonmatch-study <SHA.selection.json> <finalist-validation.json> <SHA.fitted.json GOLD> <CSV TRAIN> <grade.json> <diretório-saída-inédito>; exige LocalDB marcado/disposable.");
        var selection = Path.GetFullPath(args[1]);
        var validation = Path.GetFullPath(args[2]);
        var fit = Path.GetFullPath(args[3]);
        var csv = Path.GetFullPath(args[4]);
        var gridPath = Path.GetFullPath(args[5]);
        var output = Path.GetFullPath(args[6]);
        if (Directory.Exists(output) || File.Exists(output))
            throw new IOException("Estudo TRAIN exige saída inédita e imutável.");
        var planInfo = new FileInfo(gridPath);
        if (!planInfo.Exists || planInfo.Length is <= 0 or > 1_000_000)
            throw new InvalidDataException("Grade TRAIN ausente ou excedeu o limite.");
        var grid = JsonSerializer.Deserialize<DeclaredGrid>(
            await File.ReadAllBytesAsync(gridPath), Strict)
            ?? throw new InvalidDataException("Grade TRAIN inválida.");
        if (!grid.ManualCalibrationRequested || grid.TestUsed
            || grid.CandidateLowerThresholds is null
            || grid.CandidateLowerThresholds.Count is < 1 or > 256
            || grid.MinimumIndependentIdentityGroups < 2
            || grid.MinimumAlreadyRepresentedSources < 1)
            throw new InvalidDataException("Grade não corresponde à calibração manual TRAIN.");

        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
            ?? throw new InvalidOperationException("LocalDB descartável não informado.");
        V9GoldHarnessCommand.ValidateDisposableConnection(connection);
        var fitBytes = await File.ReadAllBytesAsync(fit);
        var snapshotSha = Hash(fitBytes);
        var snapshot = V9FittedSnapshot.Load(fitBytes, snapshotSha);
        if (snapshot.Payload.CandidateGrouping?.CandidateGrouping != "GOLD"
            || snapshot.Payload.GoldParentSnapshotSha256 is null)
            throw new InvalidDataException("É exigida variante GOLD do fit congelado.");
        if (grid.CandidateLowerThresholds.Any(x =>
                x <= 0 || x >= snapshot.Payload.Threshold)
            || grid.CandidateLowerThresholds.Distinct().Count() != grid.CandidateLowerThresholds.Count)
            throw new InvalidDataException("Pisos duplicados, inválidos ou fora do limiar superior congelado.");

        // This method performs the actual guarded, published CURRENT baseline
        // on the four physical TRAIN manager waves (plus final reevaluation).
        // It returns the ORIGINAL pre-lower-band decision and candidate frame
        // per source only in memory, never a raw per-source evidence artifact.
        var evidence = await V9GoldHarnessCommand.RunTrainStudyAsync(
            [V9GoldHarnessCommand.Flag, selection, validation, fit, csv, output],
            connection);
        if (evidence.Count == 0)
            throw new InvalidDataException("Runner não gerou evidências TRAIN físicas.");
        var evidenceHash = Hash(JsonSerializer.SerializeToUtf8Bytes(
            evidence.OrderBy(x => x.SourceFingerprintSha256, StringComparer.Ordinal)));
        var sources = evidence.Select(x => new V9LowerNonMatchTrainCalibrator.TrainingSource(
            x.SourceFingerprintSha256,
            x.IndependentBaseIdentityFingerprintSha256,
            "TRAIN_SELECTION",
            x.GoldAlreadyContainsTrueIdentity,
            IndependentlyLabeled: true,
            x.OriginalDecision,
            x.Retrieval)).ToArray();
        var policy = new V9LowerNonMatchTrainCalibrator.Policy(
            snapshotSha, evidenceHash, snapshot.Payload.Threshold,
            grid.CandidateLowerThresholds,
            grid.MinimumIndependentIdentityGroups,
            grid.MinimumAlreadyRepresentedSources,
            ManualCalibrationRequested: true, TestUsed: false);
        var study = V9LowerNonMatchTrainCalibrator.Search(policy, sources);
        var path = Path.Combine(output, "lower-train-pareto.json");
        await using (var file = new FileStream(path,
            FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(file, study, Strict);
        return study;
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
