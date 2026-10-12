using System.Collections.ObjectModel;
using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Evidence of an upstream, independently labeled TRAIN partition.
/// The caller MUST construct the frame from pairs recovered by the
/// declared blocking rules; the estimator cannot infer that fact from names.
/// The fingerprint is a provenance identifier, NOT certification.
/// </summary>
public sealed record CompositeNominalTrainingFrameV9(
    IReadOnlyList<IdentityTrainingPair> MatchedPairs,
    IReadOnlyList<IdentityTrainingPair> UnmatchedPairs,
    string BlockingFrameFingerprint,
    string LabelProvenance,
    string TrainingPartitionFingerprint);

public sealed record CompositeNominalAttributeEstimateV9(
    string Attribute,
    long MatchedObserved,
    long UnmatchedObserved,
    long MatchedMissing,
    long UnmatchedMissing,
    IReadOnlyDictionary<CompositeNominalLevelV9, long> MatchedSupport,
    IReadOnlyDictionary<CompositeNominalLevelV9, long> UnmatchedSupport,
    IReadOnlyDictionary<CompositeNominalLevelV9, decimal> M,
    IReadOnlyDictionary<CompositeNominalLevelV9, decimal> U);

/// <summary>
/// Aggregated DIAGNOSTIC TRAIN estimate, not a published model.
/// Contains no names, candidate IDs, TF, weights, thresholds or decisions.
/// </summary>
public sealed record CompositeNominalCalibrationEstimateV9(
    string NominalContractVersion,
    string BlockingFrameFingerprint,
    string LabelProvenance,
    string TrainingPartitionFingerprint,
    decimal SmoothingAlpha,
    IReadOnlyList<CompositeNominalAttributeEstimateV9> Attributes);

/// <summary>
/// V9 candidate m/u distributions over mutually exclusive full-name/component
/// levels. MISSING is explicit support only and has no LLR until a separately
/// calibrated missing-evidence contract exists. No TF correction is estimated
/// here: marginal IBGE first-name/surname frequencies are not whole-name
/// or first+last joint frequencies.
/// </summary>
public static class CompositeNominalCalibrationEstimatorV9
{
    public const string EstimatorVersion = "COMPOSITE_NOMINAL_CALIBRATION_DIAGNOSTIC_V9_1";

    private static readonly CompositeNominalLevelV9[] ObservedLevels =
        Enum.GetValues<CompositeNominalLevelV9>()
            .Where(static level => level != CompositeNominalLevelV9.Missing)
            .ToArray();

    public static CompositeNominalCalibrationEstimateV9 Estimate(
        CompositeNominalTrainingFrameV9 frame, decimal smoothingAlpha)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(frame.MatchedPairs);
        ArgumentNullException.ThrowIfNull(frame.UnmatchedPairs);
        if (string.IsNullOrWhiteSpace(frame.BlockingFrameFingerprint)
            || string.IsNullOrWhiteSpace(frame.LabelProvenance)
            || string.IsNullOrWhiteSpace(frame.TrainingPartitionFingerprint))
            throw new InvalidOperationException("V9 exige proveniência explícita de blocking, rótulos e TRAIN.");
        if (frame.MatchedPairs.Count == 0 || frame.UnmatchedPairs.Count == 0)
            throw new InvalidOperationException("V9 exige pares de ambas as classes no TRAIN.");
        if (smoothingAlpha <= 0m || smoothingAlpha > 1000m)
            throw new ArgumentOutOfRangeException(nameof(smoothingAlpha));

        var person = EstimateAttribute("NOME_V9", frame.MatchedPairs, frame.UnmatchedPairs,
            static p => CompositeNominalComparisonV9.Classify(p.LeftName, p.RightName));
        var mother = EstimateAttribute("NOME_MAE_V9", frame.MatchedPairs, frame.UnmatchedPairs,
            static p => CompositeNominalComparisonV9.Classify(p.LeftMotherName, p.RightMotherName));
        return new CompositeNominalCalibrationEstimateV9(
            CompositeNominalComparisonV9.ContractVersion,
            frame.BlockingFrameFingerprint,
            frame.LabelProvenance,
            frame.TrainingPartitionFingerprint,
            smoothingAlpha,
            Array.AsReadOnly(new[] { person, mother }));

        CompositeNominalAttributeEstimateV9 EstimateAttribute(
            string attribute,
            IReadOnlyList<IdentityTrainingPair> matched,
            IReadOnlyList<IdentityTrainingPair> unmatched,
            Func<IdentityTrainingPair, CompositeNominalLevelV9> classify)
        {
            var matchCounts = Count(matched, classify);
            var nonMatchCounts = Count(unmatched, classify);
            var matchMissing = matchCounts[CompositeNominalLevelV9.Missing];
            var nonMatchMissing = nonMatchCounts[CompositeNominalLevelV9.Missing];
            var matchObserved = matched.Count - matchMissing;
            var nonMatchObserved = unmatched.Count - nonMatchMissing;
            if (matchObserved == 0 || nonMatchObserved == 0)
                throw new InvalidOperationException(
                    "V9 não permite fabricar distribuição nominal sem suporte observado em ambas as classes: " +
                    attribute);

            return new CompositeNominalAttributeEstimateV9(
                attribute, matchObserved, nonMatchObserved, matchMissing, nonMatchMissing,
                new ReadOnlyDictionary<CompositeNominalLevelV9, long>(matchCounts),
                new ReadOnlyDictionary<CompositeNominalLevelV9, long>(nonMatchCounts),
                Smoothed(matchCounts, matchObserved, smoothingAlpha),
                Smoothed(nonMatchCounts, nonMatchObserved, smoothingAlpha));
        }
    }

    private static Dictionary<CompositeNominalLevelV9, long> Count(
        IEnumerable<IdentityTrainingPair> rows,
        Func<IdentityTrainingPair, CompositeNominalLevelV9> classify)
    {
        var counts = Enum.GetValues<CompositeNominalLevelV9>()
            .ToDictionary(static level => level, static _ => 0L);
        foreach (var row in rows)
        {
            if (row is null)
                throw new InvalidOperationException("Par TRAIN V9 nulo.");
            var level = classify(row);
            checked { counts[level]++; }
        }
        return counts;
    }

    private static IReadOnlyDictionary<CompositeNominalLevelV9, decimal> Smoothed(
        IReadOnlyDictionary<CompositeNominalLevelV9, long> counts,
        long observed,
        decimal alpha)
    {
        var denominator = observed + alpha * ObservedLevels.Length;
        var probabilities = ObservedLevels.ToDictionary(
            static level => level,
            level => (counts[level] + alpha) / denominator);
        return new ReadOnlyDictionary<CompositeNominalLevelV9, decimal>(probabilities);
    }
}
