using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Joint Gold TRAIN search contract. This is a part of the existing V9 calibrator,
/// NOT a second scorer. A physical harness must independently rerun the frozen
/// Rank/Resolve kernel for EVERY upper threshold and all three insertion orders.
/// No FS V6 VALIDATION/TEST data may be adapted into this input.
/// </summary>
public static partial class V9LowerNonMatchTrainCalibrator
{
    public const string JointVersion = "V9_GOLD_JOINT_UPPER_LOWER_TRAIN_CONTRACT_V4";
    public static readonly IReadOnlyList<string> JointOrders =
        Array.AsReadOnly(new[]
        {
            "BASELINE_G0_G1_G2_G3",
            "REVERSE_G3_G2_G1_G0",
            "INTERLEAVED_G1_G3_G0_G2"
        });

    public sealed record JointPolicy(
        string FrozenGoldFitSha256,
        string FrozenGoldSnapshotSha256,
        string TrainEvidenceSha256,
        string DeclaredGridSha256,
        string PreregisteredPlanSha256,
        IReadOnlyList<decimal> CandidateUpperThresholds,
        IReadOnlyList<decimal> CandidateLowerThresholds,
        int MinimumIndependentIdentityGroups,
        bool ManualCalibrationRequested,
        bool TestUsed);

    /// <summary>
    /// Aggregate from one physically rerun Gold TRAIN order/pair. Assertions
    /// about provenance are a harness contract, not independent attestation.
    /// </summary>
    public sealed record JointMeasurement(
        string OrderId,
        string Partition,
        string FrozenGoldFitSha256,
        string FrozenGoldSnapshotSha256,
        string PhysicalRunSha256,
        decimal UpperThreshold,
        decimal LowerThreshold,
        int IndependentIdentityGroups,
        long AcceptedLinks,
        long FalseLinks,
        long TrueLinkOpportunities,
        long FalseNewReferences,
        long PendingAfter,
        decimal BCubedF1,
        bool UpperRecomputedByPhysicalRankResolve,
        bool CompletePhysicalCandidateSearch,
        bool GoldPreWaveTruthFrozen,
        bool DisposableLocalDb);

    public sealed record JointCandidate(
        decimal UpperThreshold,
        decimal LowerThreshold,
        long FalseLinksAcrossOrders,
        long FalseNewReferencesAcrossOrders,
        long PendingAcrossOrders,
        decimal? WorstObservedFalseLinkFraction,
        int UndefinedObservedFalseLinkRateOrders,
        decimal WorstBCubedF1,
        bool IsParetoFrontier);

    /// <summary>Upper selection: accepted-link recall versus observed accepted-link FDR.</summary>
    public sealed record JointUpperCandidate(
        decimal UpperThreshold,
        decimal WorstObservedRecall,
        decimal? WorstObservedFdr,
        int UndefinedFdrCells,
        bool IsParetoFrontier);

    /// <summary>Lower selection: false physical creations versus pending, conditioned on the selected upper.</summary>
    public sealed record JointLowerCandidate(
        decimal LowerThreshold,
        long FalseNewReferencesAcrossOrders,
        long PendingAcrossOrders,
        long WorstOrderFalseNewReferences,
        long WorstOrderPending,
        bool IsParetoFrontier);

    public sealed record JointStudy(
        string Version,
        string FrozenGoldFitSha256,
        string FrozenGoldSnapshotSha256,
        string TrainEvidenceSha256,
        string DeclaredGridSha256,
        string PolicyFingerprintSha256,
        IReadOnlyList<JointCandidate> Candidates,
        decimal? SuggestedUpperThreshold,
        decimal? SuggestedLowerThreshold,
        string Status,
        bool TestUsed,
        bool OperationalActivationAllowed,
        bool StatisticallyCertified,
        IReadOnlyList<JointUpperCandidate> UpperCandidates,
        IReadOnlyList<JointLowerCandidate> LowerCandidates,
        IReadOnlyList<decimal> DescriptiveLowerFinalists);

    public static string JointGridFingerprint(
        IReadOnlyList<decimal> upper, IReadOnlyList<decimal> lower)
    {
        ArgumentNullException.ThrowIfNull(upper);
        ArgumentNullException.ThrowIfNull(lower);
        return HashJoint(JointVersion + "|UPPER|" +
            string.Join(",", upper.OrderBy(x => x).Select(DecimalJoint)) +
            "|LOWER|" + string.Join(",", lower.OrderBy(x => x).Select(DecimalJoint)));
    }

    public static string JointEvidenceFingerprint(IReadOnlyList<JointMeasurement> measurements)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        // Stable ordering and JSON property names are part of V4's evidence contract.
        return HashJoint(JsonSerializer.Serialize(measurements
            .OrderBy(x => x.UpperThreshold).ThenBy(x => x.LowerThreshold)
            .ThenBy(x => x.OrderId, StringComparer.Ordinal).ToArray()));
    }

    public static JointStudy SearchJoint(JointPolicy policy,
        IReadOnlyList<JointMeasurement> measurements)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(measurements);
        CheckSha(policy.FrozenGoldFitSha256, "Gold fit");
        CheckSha(policy.FrozenGoldSnapshotSha256, "Gold snapshot");
        CheckSha(policy.TrainEvidenceSha256, "TRAIN evidence");
        CheckSha(policy.DeclaredGridSha256, "declared grid");
        CheckSha(policy.PreregisteredPlanSha256, "preregistered plan");
        if (!policy.ManualCalibrationRequested || policy.TestUsed)
            throw new InvalidOperationException("Joint Gold calibration requires manual TRAIN only; TEST remains sealed.");
        var upper = policy.CandidateUpperThresholds;
        var lower = policy.CandidateLowerThresholds;
        if (upper is null || lower is null || upper.Count is < 2 or > 64
            || lower.Count is < 2 or > 64 || (long)upper.Count * lower.Count > 256
            || upper.Any(x => x is <= 0m or > 1m)
            || lower.Any(x => x is <= 0m or >= 1m)
            || upper.Distinct().Count() != upper.Count
            || lower.Distinct().Count() != lower.Count
            || lower.Max() >= upper.Min()
            || policy.MinimumIndependentIdentityGroups < 2)
            throw new InvalidDataException("Invalid preregistered upper/lower grid or independent-group support.");
        if (!string.Equals(JointGridFingerprint(upper, lower), policy.DeclaredGridSha256,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Grid differs from preregistered fingerprint.");
        if (measurements.Count != upper.Count * lower.Count * JointOrders.Count
            || !string.Equals(JointEvidenceFingerprint(measurements), policy.TrainEvidenceSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("TRAIN physical evidence incomplete or fingerprint mismatch.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var physicalRuns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Thresholds alter decisions, never the frozen truth universe for one
        // insertion order. Otherwise recall denominators are incomparable.
        var universeByOrder = new Dictionary<string, (long TrueLinks, int IdentityGroups)>(StringComparer.Ordinal);
        foreach (var row in measurements)
        {
            if (row is null || row.Partition != "TRAIN_SELECTION"
                || !JointOrders.Contains(row.OrderId)
                || !upper.Contains(row.UpperThreshold) || !lower.Contains(row.LowerThreshold)
                || row.LowerThreshold >= row.UpperThreshold
                || !string.Equals(row.FrozenGoldFitSha256, policy.FrozenGoldFitSha256,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row.FrozenGoldSnapshotSha256, policy.FrozenGoldSnapshotSha256,
                    StringComparison.OrdinalIgnoreCase)
                || !row.UpperRecomputedByPhysicalRankResolve
                || !row.CompletePhysicalCandidateSearch
                || !row.GoldPreWaveTruthFrozen || !row.DisposableLocalDb
                || row.IndependentIdentityGroups < policy.MinimumIndependentIdentityGroups
                || row.AcceptedLinks < 0 || row.FalseLinks < 0
                || row.FalseLinks > row.AcceptedLinks
                || row.TrueLinkOpportunities <= 0
                || row.AcceptedLinks - row.FalseLinks > row.TrueLinkOpportunities
                || row.FalseNewReferences < 0 || row.PendingAfter < 0
                || row.BCubedF1 is < 0m or > 1m)
                throw new InvalidDataException("Gold TRAIN order, physical rerun or metrics invalid.");
            CheckSha(row.PhysicalRunSha256, "physical run");
            if (universeByOrder.TryGetValue(row.OrderId, out var universe))
            {
                if (universe.TrueLinks != row.TrueLinkOpportunities
                    || universe.IdentityGroups != row.IndependentIdentityGroups)
                    throw new InvalidDataException(
                        "TRAIN frozen truth denominator or independent-group count varies across thresholds in one order.");
            }
            else
            {
                universeByOrder.Add(row.OrderId,
                    (row.TrueLinkOpportunities, row.IndependentIdentityGroups));
            }
            // A repeated run fingerprint cannot attest independent physical
            // replay for a different threshold pair or insertion order.
            if (!physicalRuns.Add(row.PhysicalRunSha256))
                throw new InvalidDataException("Physical TRAIN run reused across joint threshold cells/orders.");
            var key = DecimalJoint(row.UpperThreshold) + "|" + DecimalJoint(row.LowerThreshold)
                + "|" + row.OrderId;
            if (!seen.Add(key))
                throw new InvalidDataException("Repeated physical order/pair.");
        }

        // The full four-objective frontier is EXPLORATORY ONLY. The normative
        // selection consists of TWO separate problems (v5.00 §7.20):
        // upper = accepted-link recall × accepted-link FDR; then lower =
        // false physical creations × pendencies at the automatically chosen upper.
        // The runner still must independently attest all physical rows.
        var table = measurements.GroupBy(x => (x.UpperThreshold, x.LowerThreshold))
            .Select(g =>
            {
                var rows = g.ToArray();
                var definedRates = rows.Where(x => x.AcceptedLinks > 0)
                    .Select(x => (decimal)x.FalseLinks / x.AcceptedLinks).ToArray();
                return new JointCandidate(g.Key.UpperThreshold, g.Key.LowerThreshold,
                    rows.Sum(x => x.FalseLinks), rows.Sum(x => x.FalseNewReferences),
                    rows.Sum(x => x.PendingAfter),
                    definedRates.Length == 0 ? null : definedRates.Max(),
                    rows.Length - definedRates.Length,
                    rows.Min(x => x.BCubedF1), false);
            }).OrderBy(x => x.UpperThreshold).ThenBy(x => x.LowerThreshold).ToArray();
        var exploratory = table.Select(x => x with
        {
            IsParetoFrontier = !table.Any(y => y.FalseLinksAcrossOrders <= x.FalseLinksAcrossOrders
                && y.FalseNewReferencesAcrossOrders <= x.FalseNewReferencesAcrossOrders
                && y.PendingAcrossOrders <= x.PendingAcrossOrders
                && y.WorstBCubedF1 >= x.WorstBCubedF1
                && (y.FalseLinksAcrossOrders < x.FalseLinksAcrossOrders
                    || y.FalseNewReferencesAcrossOrders < x.FalseNewReferencesAcrossOrders
                    || y.PendingAcrossOrders < x.PendingAcrossOrders
                    || y.WorstBCubedF1 > x.WorstBCubedF1))
        }).ToArray();

        // A zero-accepted-links cell has undefined FDR, not an invented zero.
        // Upper options without ANY defined FDR cannot win an optimization
        // based on FDR. Recall denominator must be explicit for every cell.
        // Worst-case across lower sensitivities and physical orders preserves
        // the adverse order instead of silently taking an average.
        var upperTable = upper.OrderBy(x => x).Select(value =>
        {
            var rows = measurements.Where(x => x.UpperThreshold == value).ToArray();
            var definedFdr = rows.Where(x => x.AcceptedLinks > 0)
                .Select(x => (decimal)x.FalseLinks / x.AcceptedLinks).ToArray();
            var worstRecall = rows.Min(x =>
                (decimal)(x.AcceptedLinks - x.FalseLinks) / x.TrueLinkOpportunities);
            return new JointUpperCandidate(value, worstRecall,
                definedFdr.Length == 0 ? null : definedFdr.Max(),
                rows.Length - definedFdr.Length, false);
        }).ToArray();
        var measurableUppers = upperTable.Where(x => x.WorstObservedFdr.HasValue).ToArray();
        var upperFrontier = upperTable.Select(x => x with
        {
            IsParetoFrontier = x.WorstObservedFdr.HasValue
                && !measurableUppers.Any(y =>
                    y.WorstObservedRecall >= x.WorstObservedRecall
                    && y.WorstObservedFdr!.Value <= x.WorstObservedFdr!.Value
                    && (y.WorstObservedRecall > x.WorstObservedRecall
                        || y.WorstObservedFdr!.Value < x.WorstObservedFdr!.Value))
        }).ToArray();
        var upperPareto = upperFrontier.Where(x => x.IsParetoFrontier).ToArray();
        decimal? selectedUpper = null;
        if (upperPareto.Length > 0)
        {
            var minFdr = upperPareto.Min(x => x.WorstObservedFdr!.Value);
            var maxFdr = upperPareto.Max(x => x.WorstObservedFdr!.Value);
            var minRecall = upperPareto.Min(x => x.WorstObservedRecall);
            var maxRecall = upperPareto.Max(x => x.WorstObservedRecall);
            double UpperDistance(JointUpperCandidate x)
            {
                var fdr = maxFdr == minFdr ? 0d
                    : (double)(x.WorstObservedFdr!.Value - minFdr) / (double)(maxFdr - minFdr);
                var recall = maxRecall == minRecall ? 0d
                    : (double)(maxRecall - x.WorstObservedRecall) / (double)(maxRecall - minRecall);
                return fdr * fdr + recall * recall;
            }
            selectedUpper = upperPareto.OrderBy(UpperDistance)
                .ThenBy(x => x.WorstObservedFdr)
                .ThenByDescending(x => x.WorstObservedRecall)
                .ThenByDescending(x => x.UpperThreshold)
                .First().UpperThreshold;
        }

        // Lower values are evaluated ONLY at the upper selected by the first
        // problem. Do not use link FDR, recall or B-cubed as lower objectives.
        JointLowerCandidate[] lowerFrontier = [];
        decimal? selectedLower = null;
        decimal[] finalists = [];
        if (selectedUpper.HasValue)
        {
            var lowerTable = measurements.Where(x => x.UpperThreshold == selectedUpper.Value)
                .GroupBy(x => x.LowerThreshold)
                .Select(g =>
                {
                    var rows = g.ToArray();
                    return new JointLowerCandidate(g.Key,
                        rows.Sum(x => x.FalseNewReferences), rows.Sum(x => x.PendingAfter),
                        rows.Max(x => x.FalseNewReferences), rows.Max(x => x.PendingAfter),
                        false);
                }).OrderBy(x => x.LowerThreshold).ToArray();
            lowerFrontier = lowerTable.Select(x => x with
            {
                IsParetoFrontier = !lowerTable.Any(y =>
                    y.FalseNewReferencesAcrossOrders <= x.FalseNewReferencesAcrossOrders
                    && y.PendingAcrossOrders <= x.PendingAcrossOrders
                    && (y.FalseNewReferencesAcrossOrders < x.FalseNewReferencesAcrossOrders
                        || y.PendingAcrossOrders < x.PendingAcrossOrders))
            }).ToArray();
            var lowerPareto = lowerFrontier.Where(x => x.IsParetoFrontier).ToArray();
            var minCreates = lowerPareto.Min(x => x.FalseNewReferencesAcrossOrders);
            var maxCreates = lowerPareto.Max(x => x.FalseNewReferencesAcrossOrders);
            var minPending = lowerPareto.Min(x => x.PendingAcrossOrders);
            var maxPending = lowerPareto.Max(x => x.PendingAcrossOrders);
            double LowerDistance(JointLowerCandidate x)
            {
                var creates = maxCreates == minCreates ? 0d
                    : (double)(x.FalseNewReferencesAcrossOrders - minCreates) / (maxCreates - minCreates);
                var pending = maxPending == minPending ? 0d
                    : (double)(x.PendingAcrossOrders - minPending) / (maxPending - minPending);
                return creates * creates + pending * pending;
            }
            selectedLower = lowerPareto.OrderBy(LowerDistance)
                .ThenBy(x => x.FalseNewReferencesAcrossOrders)
                .ThenBy(x => x.PendingAcrossOrders)
                .ThenBy(x => x.LowerThreshold).First().LowerThreshold;
            // Descriptive robust finalists: knee, fewest false creations,
            // fewest pendencies. No preset error budget or percentage cutoff.
            finalists = new[]
            {
                selectedLower.Value,
                lowerPareto.OrderBy(x => x.FalseNewReferencesAcrossOrders)
                    .ThenBy(x => x.WorstOrderFalseNewReferences)
                    .ThenBy(x => x.PendingAcrossOrders)
                    .ThenBy(x => x.LowerThreshold).First().LowerThreshold,
                lowerPareto.OrderBy(x => x.PendingAcrossOrders)
                    .ThenBy(x => x.WorstOrderPending)
                    .ThenBy(x => x.FalseNewReferencesAcrossOrders)
                    .ThenBy(x => x.LowerThreshold).First().LowerThreshold
            }.Distinct().ToArray();
        }

        return new JointStudy(JointVersion, policy.FrozenGoldFitSha256,
            policy.FrozenGoldSnapshotSha256, policy.TrainEvidenceSha256,
            policy.DeclaredGridSha256,
            HashJoint(JsonSerializer.Serialize(policy)),
            Array.AsReadOnly(exploratory), selectedUpper, selectedLower,
            selectedUpper.HasValue
                ? "TRAIN_TWO_STAGE_UPPER_RECALL_FDR_LOWER_CREATIONS_PENDING_UNCERTIFIED_PHYSICAL_ATTESTATION_REQUIRED"
                : "TRAIN_INSUFFICIENT_DEFINED_UPPER_FDR_NO_SELECTION_UNCERTIFIED",
            TestUsed: false, OperationalActivationAllowed: false,
            StatisticallyCertified: false,
            UpperCandidates: Array.AsReadOnly(upperFrontier),
            LowerCandidates: Array.AsReadOnly(lowerFrontier),
            DescriptiveLowerFinalists: Array.AsReadOnly(finalists));
    }

    private static string DecimalJoint(decimal x) =>
        x.ToString("G29", CultureInfo.InvariantCulture);

    private static string HashJoint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
