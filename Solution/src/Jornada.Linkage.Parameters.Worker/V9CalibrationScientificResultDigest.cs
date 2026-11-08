using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// A scientific-result digest deliberately independent of runner, elapsed time and
/// MaximumParallelRuleSets. This is a regression witness, NOT statistical certification.
/// </summary>
public sealed record V9CalibrationScientificResultRow(
    string OptionId, decimal ConflictMargin, long TruePositive, long FalsePositive,
    long FalseNegative, long Conflicts, IReadOnlyDictionary<string, long> ReasonsByResolveStatus,
    string FittedSnapshotSha256);

public static class V9CalibrationScientificResultDigest
{
    public const string Schema = "V9_SCIENTIFIC_RESULT_DIGEST_V1";

    public static string FromStudy(V9AdaptiveCalibrationStudyResult study)
    {
        ArgumentNullException.ThrowIfNull(study);
        // Key is strictly scientific. The plan SHA intentionally includes execution
        // controls and is stored separately; it cannot serve as this digest.
        var fits = study.FrozenConfigurations.ToDictionary(
            x => (x.OptionId, x.ConflictMargin), x => x.Snapshot.ContentSha256);
        var rows = study.SourceMeasurements.Select(x =>
        {
            if (!fits.TryGetValue((x.OptionId, x.ConflictMargin), out var snapshot))
                throw new InvalidDataException("Medição sem snapshot físico correspondente.");
            var fn = checked(x.EligibleSourceObservationsBeforeBlocking - x.TrueAcceptedSourceObservations);
            return new V9CalibrationScientificResultRow(x.OptionId, x.ConflictMargin,
                x.TrueAcceptedSourceObservations, x.FalseAcceptedSourceObservations,
                fn, x.Conflicts, x.ReasonsByResolveStatus, snapshot);
        });
        // InitialSuggestedOptionId is an operational P95-gated recommendation:
        // it changes with host load and must NOT alter the scientific hash.
        return Compute(rows, study.Pareto.ExploratorySuggestedOptionId);
    }

    public static string Compute(IEnumerable<V9CalibrationScientificResultRow> rows,
        string? scientificProposedOptionId)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var sorted = rows.OrderBy(x => x.OptionId, StringComparer.Ordinal)
            .ThenBy(x => x.ConflictMargin).ToArray();
        if (sorted.Length == 0 || sorted.Any(x => x is null
            || string.IsNullOrWhiteSpace(x.OptionId)
            || x.FittedSnapshotSha256 is not { Length: 64 }
            || !x.FittedSnapshotSha256.All(Uri.IsHexDigit)
            || x.TruePositive < 0 || x.FalsePositive < 0 || x.FalseNegative < 0
            || x.Conflicts < 0 || x.ReasonsByResolveStatus is null
            || x.ReasonsByResolveStatus.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0)))
            throw new InvalidDataException("Resultado científico inválido para digest.");
        if (sorted.Zip(sorted.Skip(1), (a, b) =>
                a.OptionId == b.OptionId && a.ConflictMargin == b.ConflictMargin).Any(x => x))
            throw new InvalidDataException("Configuração científica duplicada no digest.");
        var canonical = new
        {
            Schema, ScientificProposedOptionId = scientificProposedOptionId,
            Rows = sorted.Select(x => new
            {
                x.OptionId, x.ConflictMargin, x.TruePositive, x.FalsePositive,
                x.FalseNegative, x.Conflicts, x.FittedSnapshotSha256,
                ReasonsByResolveStatus = x.ReasonsByResolveStatus
                    .OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => new { p.Key, p.Value }).ToArray()
            }).ToArray()
        };
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical)))).ToLowerInvariant();
    }
}
