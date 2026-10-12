using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Read-only bridge from actual SQL blocking-conditioned U samples and
/// independently-labeled, inter-manager M pairs into the V9 TRAIN diagnostic.
/// This is NOT a calibrated V9 model: unlike U, the inter-manager M sample
/// has not been proven to survive the selected blocking rules. A declared
/// fingerprint proves only which rules were requested, not sample membership.
/// </summary>
public sealed record CompositeNominalBlockingAuditV9(
    string Status,
    string BlockingConfigurationSha256,
    string NominalContractVersion,
    long CandidatePoolSize,
    long MatchedInputCount,
    long UnmatchedSampleCount,
    IReadOnlyList<CompositeNominalAttributeEstimateV9> Attributes,
    long MatchedLogicallyRetained = 0)
{
    public bool Available => Status is "DIAGNOSTIC_ONLY_M_NOT_BLOCK_CONDITIONED"
        or CompositeNominalBlockingAuditBuilderV9.LogicalConditioningStatus;
}

public static class CompositeNominalBlockingAuditBuilderV9
{
    public const string Version = "COMPOSITE_NOMINAL_BLOCKING_TRAIN_AUDIT_V9_1";
    public const string MissingSupportStatus = "UNAVAILABLE_INSUFFICIENT_NOMINAL_SUPPORT";
    public const string MissingMatchedSupportStatus = "UNAVAILABLE_NO_LOGICALLY_RETAINED_MATCHES";
    // SQL gold blocking keys may include historical VERSIONED_ALIAS values which
    // cannot be attested by the current-value in-memory projection alone.
    public const string LogicalConditioningStatus = "DIAGNOSTIC_ONLY_M_LOGICAL_RULESET_U_SQL_CONDITIONED";

    public static CompositeNominalBlockingAuditV9 Build(
        IReadOnlyList<IdentityTrainingPair> independentlyMatchedPairs,
        BlockingConditionedUnmatchedPairSample unmatched,
        IReadOnlyCollection<LinkageBlockingPass> selectedPasses,
        string normalizationVersion,
        string projectionSchema,
        string projectionFingerprint,
        decimal smoothingAlpha,
        bool conditionMatchedPairsOnLogicalBlocking = false)
    {
        ArgumentNullException.ThrowIfNull(independentlyMatchedPairs);
        ArgumentNullException.ThrowIfNull(unmatched);
        ArgumentNullException.ThrowIfNull(unmatched.Pairs);
        ArgumentNullException.ThrowIfNull(selectedPasses);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizationVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionSchema);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionFingerprint);

        if (selectedPasses.Count == 0)
            throw new ArgumentException("Amostra V9 exige passes de blocking.", nameof(selectedPasses));
        if (unmatched.CandidatePoolSize < unmatched.Pairs.Count)
            throw new InvalidOperationException("Amostra U V9 excede pool candidato.");
        if (independentlyMatchedPairs.Count == 0 || unmatched.Pairs.Count == 0)
            throw new InvalidOperationException("Amostra TRAIN V9 exige pares M e U.");
        if (smoothingAlpha <= 0m || smoothingAlpha > 1000m)
            throw new ArgumentOutOfRangeException(nameof(smoothingAlpha));

        var passes = selectedPasses.Select(p => LinkageBlockingPass.Create(p.PassId, p.Fields))
            .OrderBy(p => p.PassId, StringComparer.Ordinal).ToArray();
        if (passes.Select(p => p.PassId).Distinct(StringComparer.Ordinal).Count() != passes.Length)
            throw new InvalidOperationException("Pass IDs duplicados no ruleset V9.");
        var allowed = BlockingCandidateFeatureCatalog.RequiredCalibratorCandidates.ToHashSet(StringComparer.Ordinal);
        if (passes.SelectMany(p => p.Fields).Any(feature => !allowed.Contains(feature)))
            throw new InvalidOperationException("Feature V9 fora da projeção canônica do calibrador.");

        // This is the same AND-within-pass, OR-between-passes algebra as the
        // optimizer/runner. Current-value projections approximate logical
        // recovery but DO NOT prove physical SQL blocking-key membership.
        // Neither positive/negative class labels nor scores affect retention.
        var matchesForEstimate = conditionMatchedPairsOnLogicalBlocking
            ? independentlyMatchedPairs.Where(pair =>
            {
                var observation = BlockingFeatureObservationFactory.Create(pair, isReferenceMatch: true);
                return passes.Any(pass => pass.Fields.All(field =>
                    observation.Agreements.TryGetValue(field, out var agreement) && agreement is true));
            }).ToArray()
            : independentlyMatchedPairs;

        // SHA of the intended ruleset configuration ONLY, never of names/PII.
        // Does not attest that the M pairs satisfy these passes.
        var canonical = new StringBuilder()
            .Append(Version).Append('\n')
            .Append(normalizationVersion).Append('\n')
            .Append(projectionSchema).Append('\n')
            .Append(projectionFingerprint.ToLowerInvariant()).Append('\n');
        foreach (var pass in passes)
        {
            canonical.Append("PASS\t").Append(pass.PassId).Append('\n');
            foreach (var field in pass.Fields)
                canonical.Append("F\t").Append(field).Append('\n');
        }
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();

        if (matchesForEstimate.Count == 0)
            return new CompositeNominalBlockingAuditV9(
                MissingMatchedSupportStatus, fingerprint,
                CompositeNominalComparisonV9.ContractVersion,
                unmatched.CandidatePoolSize, independentlyMatchedPairs.Count,
                unmatched.Pairs.Count, Array.Empty<CompositeNominalAttributeEstimateV9>(),
                MatchedLogicallyRetained: 0);

        try
        {
            var frame = new CompositeNominalTrainingFrameV9(
                matchesForEstimate,
                unmatched.Pairs,
                fingerprint,
                conditionMatchedPairsOnLogicalBlocking
                    ? "M_INTERMANAGER_INDEPENDENT_CPF_LOGICAL_BLOCKING_ONLY_U_SQL_CONDITIONED_DIAGNOSTIC"
                    : "M_INTERMANAGER_INDEPENDENT_CPF_AND_U_BLOCKING_CONDITIONED_DIAGNOSTIC_ONLY",
                "SAMPLED_TRAIN_UPSTREAM_PARTITION_NOT_A_CERTIFICATION");
            var estimate = CompositeNominalCalibrationEstimatorV9.Estimate(frame, smoothingAlpha);
            return new CompositeNominalBlockingAuditV9(
                conditionMatchedPairsOnLogicalBlocking
                    ? LogicalConditioningStatus : "DIAGNOSTIC_ONLY_M_NOT_BLOCK_CONDITIONED",
                fingerprint,
                estimate.NominalContractVersion,
                unmatched.CandidatePoolSize,
                independentlyMatchedPairs.Count,
                unmatched.Pairs.Count,
                estimate.Attributes,
                MatchedLogicallyRetained: conditionMatchedPairsOnLogicalBlocking
                    ? matchesForEstimate.Count : 0);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("suporte observado", StringComparison.Ordinal))
        {
            // Insufficient mother/person evidence must NOT jeopardize historic
            // V8 GENERATE_DRAFT, but V9 cannot use partial/fictitious m/u.
            return new CompositeNominalBlockingAuditV9(
                MissingSupportStatus, fingerprint,
                CompositeNominalComparisonV9.ContractVersion,
                unmatched.CandidatePoolSize,
                independentlyMatchedPairs.Count,
                unmatched.Pairs.Count,
                Array.Empty<CompositeNominalAttributeEstimateV9>(),
                MatchedLogicallyRetained: conditionMatchedPairsOnLogicalBlocking
                    ? matchesForEstimate.Count : 0);
        }
    }
}
