using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Immutable declaration of the V9 two-dimensional TRAIN threshold search.
/// This is a preregistration artifact, NOT a fitted model or an authorization
/// to publish. Upper-threshold changes require rescoring the frozen Gold
/// evidence; the lower-only preview cannot estimate their link-error impact.
/// </summary>
public static class V9JointThresholdGrid
{
    public const string Version = "V9_JOINT_TRAIN_GRID_V1";

    public sealed record Pair(decimal Lower, decimal Upper);

    public sealed record Declaration(
        string FrozenSnapshotSha256,
        string TrainEvidenceSha256,
        IReadOnlyList<Pair> Pairs,
        string GridSha256,
        string DeclarationSha256,
        bool UsesTestForSelection,
        bool PermitsAutomaticActivation);

    public static Declaration Preregister(
        string frozenSnapshotSha256,
        string trainEvidenceSha256,
        IReadOnlyList<decimal> lowerThresholds,
        IReadOnlyList<decimal> upperThresholds)
    {
        CheckSha(frozenSnapshotSha256, nameof(frozenSnapshotSha256));
        CheckSha(trainEvidenceSha256, nameof(trainEvidenceSha256));
        ArgumentNullException.ThrowIfNull(lowerThresholds);
        ArgumentNullException.ThrowIfNull(upperThresholds);
        if (lowerThresholds.Count is < 1 or > 256 ||
            upperThresholds.Count is < 1 or > 256 ||
            (long)lowerThresholds.Count * upperThresholds.Count > 4096 ||
            lowerThresholds.Any(x => x <= 0m || x >= 1m) ||
            upperThresholds.Any(x => x <= 0m || x >= 1m) ||
            lowerThresholds.Distinct().Count() != lowerThresholds.Count ||
            upperThresholds.Distinct().Count() != upperThresholds.Count)
            throw new InvalidDataException("Invalid preregistered V9 TRAIN grid.");

        var pairs = (from upper in upperThresholds.OrderBy(x => x)
                     from lower in lowerThresholds.OrderBy(x => x)
                     where lower < upper
                     select new Pair(lower, upper)).ToArray();
        if (pairs.Length == 0)
            throw new InvalidDataException("No admissible lower < upper pairs.");
        // Reject silently discarded invalid Cartesian combinations.
        if (pairs.Length != (long)lowerThresholds.Count * upperThresholds.Count)
            throw new InvalidDataException("Every declared pair must satisfy 0 < lower < upper < 1.");

        var canonical = string.Join("\n", pairs.Select(x =>
            x.Lower.ToString(CultureInfo.InvariantCulture) + "|" +
            x.Upper.ToString(CultureInfo.InvariantCulture)));
        var gridSha = Sha(canonical);
        var declarationSha = Sha(Version + "|" +
            frozenSnapshotSha256.ToLowerInvariant() + "|" +
            trainEvidenceSha256.ToLowerInvariant() + "|" + gridSha);
        return new Declaration(frozenSnapshotSha256.ToLowerInvariant(),
            trainEvidenceSha256.ToLowerInvariant(), pairs, gridSha,
            declarationSha, UsesTestForSelection: false,
            PermitsAutomaticActivation: false);
    }

    private static string Sha(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void CheckSha(string? value, string label)
    {
        if (value is null || value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Invalid SHA-256: " + label);
    }
}
