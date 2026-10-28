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
    public const string JointVersion = "V9_GOLD_JOINT_UPPER_LOWER_TRAIN_CONTRACT_V1";
    public static readonly IReadOnlyList<string> JointOrders =
        Array.AsReadOnly(new[] { "G0_G3", "G3_G0", "G1_G3_G0_G2" });

    public sealed record JointPolicy(
        string FrozenGoldFitSha256,
        string FrozenGoldSnapshotSha256,
        string TrainEvidenceSha256,
        string DeclaredGridSha256,
        string PreregisteredPlanSha256,
        IReadOnlyList<decimal> CandidateUpperThresholds,
        IReadOnlyList<decimal> CandidateLowerThresholds,
        decimal MaximumObservedFalseLinkFraction,
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
        decimal WorstObservedFalseLinkFraction,
        decimal WorstBCubedF1,
        bool MeetsPredeclaredObservedRiskConstraint,
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
        bool StatisticallyCertified);

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
        // Stable ordering and JSON property names are part of V1's evidence contract.
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
            || policy.MaximumObservedFalseLinkFraction is < 0m or > 1m
            || policy.MinimumIndependentIdentityGroups < 2)
            throw new InvalidDataException("Invalid preregistered upper/lower grid or observed-risk constraint.");
        if (!string.Equals(JointGridFingerprint(upper, lower), policy.DeclaredGridSha256,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Grid differs from preregistered fingerprint.");
        if (measurements.Count != upper.Count * lower.Count * JointOrders.Count
            || !string.Equals(JointEvidenceFingerprint(measurements), policy.TrainEvidenceSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("TRAIN physical evidence incomplete or fingerprint mismatch.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
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
                || row.FalseNewReferences < 0 || row.PendingAfter < 0
                || row.BCubedF1 is < 0m or > 1m)
                throw new InvalidDataException("Gold TRAIN order, physical rerun or metrics invalid.");
            CheckSha(row.PhysicalRunSha256, "physical run");
            var key = DecimalJoint(row.UpperThreshold) + "|" + DecimalJoint(row.LowerThreshold)
                + "|" + row.OrderId;
            if (!seen.Add(key))
                throw new InvalidDataException("Repeated physical order/pair.");
        }

        var table = measurements.GroupBy(x => (x.UpperThreshold, x.LowerThreshold))
            .Select(g =>
            {
                var rows = g.ToArray();
                var eligible = rows.All(x => x.AcceptedLinks > 0
                    && (decimal)x.FalseLinks / x.AcceptedLinks
                        <= policy.MaximumObservedFalseLinkFraction);
                return new JointCandidate(g.Key.UpperThreshold, g.Key.LowerThreshold,
                    rows.Sum(x => x.FalseLinks), rows.Sum(x => x.FalseNewReferences),
                    rows.Sum(x => x.PendingAfter),
                    rows.Max(x => x.AcceptedLinks == 0 ? 1m
                        : (decimal)x.FalseLinks / x.AcceptedLinks),
                    rows.Min(x => x.BCubedF1), eligible, false);
            }).OrderBy(x => x.UpperThreshold).ThenBy(x => x.LowerThreshold).ToArray();
        var admissible = table.Where(x => x.MeetsPredeclaredObservedRiskConstraint).ToArray();
        var frontier = table.Select(x => x with
        {
            IsParetoFrontier = x.MeetsPredeclaredObservedRiskConstraint
                && !admissible.Any(y => y.FalseLinksAcrossOrders <= x.FalseLinksAcrossOrders
                    && y.FalseNewReferencesAcrossOrders <= x.FalseNewReferencesAcrossOrders
                    && y.PendingAcrossOrders <= x.PendingAcrossOrders
                    && y.WorstBCubedF1 >= x.WorstBCubedF1
                    && (y.FalseLinksAcrossOrders < x.FalseLinksAcrossOrders
                        || y.FalseNewReferencesAcrossOrders < x.FalseNewReferencesAcrossOrders
                        || y.PendingAcrossOrders < x.PendingAcrossOrders
                        || y.WorstBCubedF1 > x.WorstBCubedF1))
        }).ToArray();
        // Deterministic descriptive suggestion, NEVER a safety approval or
        // automatic publication. A statistical FDR gate is still required.
        var suggestion = frontier.Where(x => x.IsParetoFrontier)
            .OrderBy(x => x.FalseLinksAcrossOrders)
            .ThenBy(x => x.FalseNewReferencesAcrossOrders)
            .ThenBy(x => x.PendingAcrossOrders)
            .ThenByDescending(x => x.WorstBCubedF1)
            .ThenBy(x => x.UpperThreshold).ThenBy(x => x.LowerThreshold)
            .FirstOrDefault();
        return new JointStudy(JointVersion, policy.FrozenGoldFitSha256,
            policy.FrozenGoldSnapshotSha256, policy.TrainEvidenceSha256,
            policy.DeclaredGridSha256,
            HashJoint(JsonSerializer.Serialize(policy)),
            Array.AsReadOnly(frontier), suggestion?.UpperThreshold,
            suggestion?.LowerThreshold,
            suggestion is null ? "TRAIN_NO_PAIR_WITHIN_PREDECLARED_OBSERVED_RISK_CONSTRAINT"
                : "TRAIN_DESCRIPTIVE_PAIR_ONLY_UNCERTIFIED_REQUIRES_INDEPENDENT_VALIDATION",
            TestUsed: false, OperationalActivationAllowed: false,
            StatisticallyCertified: false);
    }

    private static string DecimalJoint(decimal x) =>
        x.ToString("G29", CultureInfo.InvariantCulture);

    private static string HashJoint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
