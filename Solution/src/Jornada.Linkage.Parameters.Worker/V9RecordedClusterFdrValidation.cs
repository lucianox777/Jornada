using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Source-observation to identity-base binding from the pre-blocking cohort.
/// The same source identity may have multiple query observations. Fingerprints
/// are opaque: no names, CPF or UUID are stored in the report.
/// </summary>
public sealed record V9RecordedBaseObservation(
    string ObservationFingerprintSha256,
    string BaseIdentityFingerprintSha256);

public sealed record V9RecordedClusterFdrValidationReport(
    string Version,
    string RecordedEvaluationFingerprintSha256,
    string BoundCohortEvidenceFingerprintSha256,
    int PreBlockingBaseIdentities,
    int IdentitiesWithoutAcceptedLinks,
    int IdentitiesWithoutAnyQueryObservation,
    V9IdentityClusterFdr.Summary DescriptiveFdr,
    V9IdentityClusterFdr.ClusterMeanRatioBound ConditionalClusterUpper,
    bool OperationallyCertified);

/// <summary>
/// Reuses the existing EvaluateRecorded runtime-outcome evaluator rather than
/// redeciding posterior thresholds. VALIDATION-only; TEST remains sealed.
/// A population mean-ratio confidence bound requires independently sampled
/// identities and a predeclared, enforced cap; those assumptions cannot be
/// proven by this adapter and do NOT authorize operational certification.
/// </summary>
public static class V9RecordedClusterFdrValidation
{
    public const string Version = "V9_RECORDED_FINAL_IDENTITY_CLUSTER_FDR_VALIDATION_V2";

    public static V9RecordedClusterFdrValidationReport Evaluate(
        IndependentRuleSetEvaluationManifest manifest,
        IReadOnlyList<IndependentResolutionObservation> observations,
        IReadOnlyList<IndependentRecordedOutcome> recordedOutcomes,
        IReadOnlyCollection<string> preBlockingIdentityFingerprints,
        IReadOnlyList<V9RecordedBaseObservation> sourceIdentityBindings,
        Guid recordedRunId,
        string recordedModelVersion,
        string recordedRuleSetFingerprintSha256,
        decimal threshold,
        decimal conflictMargin,
        bool completeCandidateUniverse,
        int maximumAcceptedLinksPerBaseIdentity,
        double alpha = 0.05)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(recordedOutcomes);
        ArgumentNullException.ThrowIfNull(preBlockingIdentityFingerprints);
        ArgumentNullException.ThrowIfNull(sourceIdentityBindings);

        var cohort = preBlockingIdentityFingerprints.ToArray();
        if (cohort.Length < 2 || cohort.Any(x => !IsSha(x))
            || cohort.Distinct(StringComparer.Ordinal).Count() != cohort.Length)
            throw new InvalidDataException("O censo pré-blocking exige identidades-base únicas e válidas.");
        if (sourceIdentityBindings.Count != observations.Count
            || sourceIdentityBindings.Any(x => x is null
                || !IsSha(x.ObservationFingerprintSha256)
                || !IsSha(x.BaseIdentityFingerprintSha256))
            || sourceIdentityBindings.Select(x => x.ObservationFingerprintSha256)
                .Distinct(StringComparer.Ordinal).Count() != sourceIdentityBindings.Count)
            throw new InvalidDataException("Vínculos de origem por observação incompletos ou duplicados.");

        var cohortSet = cohort.ToHashSet(StringComparer.Ordinal);
        var binding = sourceIdentityBindings.ToDictionary(
            x => x.ObservationFingerprintSha256,
            x => x.BaseIdentityFingerprintSha256, StringComparer.Ordinal);
        if (binding.Values.Any(x => !cohortSet.Contains(x))
            || observations.Any(x => x is null
                || !binding.ContainsKey(x.ObservationFingerprintSha256)))
            throw new InvalidDataException("Observação associada a identidade ausente do censo pré-blocking.");

        // The independent evaluator validates manifest denominators, all final
        // decisions, complete SQL candidate universe, model/ruleset fingerprints,
        // conflict status and run identity before any cluster aggregation.
        var recorded = IndependentResolutionEvaluator.EvaluateRecorded(
            manifest, observations, recordedOutcomes, recordedRunId,
            recordedModelVersion, recordedRuleSetFingerprintSha256,
            threshold, conflictMargin, completeCandidateUniverse);

        var byObservation = recordedOutcomes.ToDictionary(
            x => x.ObservationFingerprintSha256, StringComparer.Ordinal);
        var accepted = cohort.ToDictionary(x => x,
            _ => (True: 0L, False: 0L), StringComparer.Ordinal);
        foreach (var row in observations)
        {
            var outcome = byObservation[row.ObservationFingerprintSha256];
            if (outcome.Status != "RESOLVIDO")
                continue;

            var group = binding[row.ObservationFingerprintSha256];
            var counts = accepted[group];
            if (outcome.ResolvedCandidateFingerprintSha256
                == row.ReferenceCandidateFingerprintSha256)
                counts.True = checked(counts.True + 1);
            else
                counts.False = checked(counts.False + 1);
            accepted[group] = counts;
        }
        var groups = accepted.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new V9IdentityClusterFdr.IdentityGroup(
                x.Key, x.Value.True, x.Value.False)).ToArray();
        var descriptive = V9IdentityClusterFdr.Describe(groups);
        if (descriptive.TrueAccepted != recorded.Overall.TrueLinks
            || descriptive.FalseAccepted != recorded.Overall.FalseLinks)
            throw new InvalidDataException("TP/FP agrupados divergem das decisões finais registradas.");

        var bound = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatio(
            groups, maximumAcceptedLinksPerBaseIdentity, alpha);
        var observedGroups = binding.Values.ToHashSet(StringComparer.Ordinal);
        var evidenceFingerprint = BoundEvidenceFingerprint(
            recorded.FingerprintSha256, cohort, binding,
            maximumAcceptedLinksPerBaseIdentity, alpha);
        return new V9RecordedClusterFdrValidationReport(
            Version, recorded.FingerprintSha256, evidenceFingerprint, cohort.Length,
            groups.Count(x => x.TrueAccepted + x.FalseAccepted == 0),
            cohort.Length - observedGroups.Count, descriptive, bound,
            OperationallyCertified: false);
    }

    /// <summary>
    /// Binds the final recorded run to the WHOLE pre-blocking sampling frame,
    /// the explicit source-to-base attribution, and the bound's fixed cap/alpha.
    /// A digest prevents silent substitution in exported evidence but by itself
    /// DOES NOT authenticate the underlying data or attest iid sampling.
    /// </summary>
    public static string BoundEvidenceFingerprint(
        string recordedEvaluationFingerprintSha256,
        IReadOnlyCollection<string> cohort,
        IReadOnlyDictionary<string, string> bindings,
        int maximumAcceptedLinksPerBaseIdentity,
        double alpha)
    {
        ArgumentNullException.ThrowIfNull(cohort);
        ArgumentNullException.ThrowIfNull(bindings);
        if (!IsSha(recordedEvaluationFingerprintSha256)
            || cohort.Count < 2 || cohort.Any(x => !IsSha(x))
            || cohort.Distinct(StringComparer.Ordinal).Count() != cohort.Count
            || bindings.Any(x => !IsSha(x.Key) || !IsSha(x.Value)
                || !cohort.Contains(x.Value, StringComparer.Ordinal))
            || maximumAcceptedLinksPerBaseIdentity < 1
            || !double.IsFinite(alpha) || alpha <= 0 || alpha >= 1)
            throw new InvalidDataException("Evidência agrupada não pode ser identificada de forma íntegra.");

        var canonical = new StringBuilder()
            .Append(Version).Append('\n')
            .Append(recordedEvaluationFingerprintSha256).Append('\n')
            .Append(maximumAcceptedLinksPerBaseIdentity).Append('|')
            .Append(alpha.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
            .Append('\n');
        foreach (var identity in cohort.OrderBy(x => x, StringComparer.Ordinal))
            canonical.Append("GROUP:").Append(identity).Append('\n');
        foreach (var pair in bindings.OrderBy(x => x.Key, StringComparer.Ordinal))
            canonical.Append("SOURCE:").Append(pair.Key).Append('|')
                .Append(pair.Value).Append('\n');
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static bool IsSha(string? value) =>
        value is { Length: 64 } && value.All(c =>
            c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
