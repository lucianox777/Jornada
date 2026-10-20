using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Identity-wise deterministic internal split. Never accepts external holdouts.</summary>
public static class V9InternalTrainingSplit
{
    public const string Version = "V9_TRAIN_ADJUST_SELECTION_IDENTITY_SPLIT_V1";
    public sealed record Assignment(string BaseIdentity, string Purpose);
    public sealed record Manifest(string Version, string Seed, decimal AdjustmentFraction,
        string OriginalTrainSha256, string AssignmentSha256, IReadOnlyList<Assignment> Assignments);
    public sealed record Result(Manifest Manifest,
        IReadOnlyList<V9PhysicalCalibrationObservation> Adjustment,
        IReadOnlyList<V9PhysicalCalibrationObservation> Selection);

    public static Result Create(IReadOnlyCollection<V9PhysicalCalibrationObservation> train,
        string seed, decimal adjustmentFraction = .7m)
    {
        ArgumentNullException.ThrowIfNull(train);
        if (string.IsNullOrWhiteSpace(seed) || adjustmentFraction is <= 0 or >= 1
            || train.Count < 4 || train.Any(x => x is null || x.Partition != "TRAIN"
                || string.IsNullOrWhiteSpace(x.BasePersonId) || string.IsNullOrWhiteSpace(x.Gestor) || x.ObservationId <= 0)
            || train.Select(x => x.ObservationId).Distinct().Count() != train.Count)
            throw new InvalidDataException("Partição interna aceita somente TRAIN válido, sem VALIDATION/TEST.");
        var identities = train.Select(x => x.BasePersonId).Distinct(StringComparer.Ordinal)
            .OrderBy(id => Hash(JsonSerializer.Serialize(new[] { Version, seed, id })), StringComparer.Ordinal)
            .ThenBy(id => id, StringComparer.Ordinal).ToArray();
        var count = checked((int)decimal.Floor(identities.Length * adjustmentFraction));
        if (count < 2 || identities.Length - count < 2)
            throw new InvalidDataException("Cada partição interna exige ao menos duas identidades.");
        var fitIds = identities.Take(count).ToHashSet(StringComparer.Ordinal);
        var assignments = identities.OrderBy(x => x, StringComparer.Ordinal)
            .Select(id => new Assignment(id, fitIds.Contains(id) ? "TRAIN_ADJUST" : "TRAIN_SELECTION")).ToArray();
        var ordered = train.OrderBy(x => x.ObservationId).ToArray();
        return new(new(Version, seed, adjustmentFraction, Hash(JsonSerializer.Serialize(ordered)),
            Hash(JsonSerializer.Serialize(assignments)), Array.AsReadOnly(assignments)),
            Array.AsReadOnly(ordered.Where(x => fitIds.Contains(x.BasePersonId)).ToArray()),
            Array.AsReadOnly(ordered.Where(x => !fitIds.Contains(x.BasePersonId)).ToArray()));
    }

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
