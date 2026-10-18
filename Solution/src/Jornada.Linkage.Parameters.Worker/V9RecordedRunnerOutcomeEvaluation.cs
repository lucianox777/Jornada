using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// One diagnostic report bound to the Runner's already final C# decisions and
/// the whole PRE-blocking base-identity frame. This is NOT a production
/// certification or an authenticated HML extract.
/// </summary>
public sealed record V9RecordedRunnerBoundEvaluationReport(
    string Version,
    string Partition,
    Guid RunId,
    Guid ModelId,
    int PreBlockingBaseIdentities,
    int SourceObservations,
    string RecordedRunnerEvidenceFingerprintSha256,
    V9RecordedClusterFdrValidationReport ClusterFdr,
    bool OperationallyCertified);

/// <summary>
/// Reduces the gap between a raw recorded C# Runner decision and the final
/// independent identity-cluster report. No TP/FP, final status, or candidate
/// ranking is recomputed here: downstream EvaluateRecorded uses exactly the
/// post-guard status supplied via the Bind adapter. Only VALIDATION is valid.
/// </summary>
public static class V9RecordedRunnerOutcomeEvaluation
{
    public const string Version = "V9_RUNNER_RECORDED_BOUND_CLUSTER_FDR_PIPELINE_V1";

    public static V9RecordedRunnerBoundEvaluationReport EvaluateValidation(
        IndependentRuleSetEvaluationManifest manifest,
        IReadOnlyList<V9RecordedRunnerObservation> recordedRunnerObservations,
        IReadOnlyList<IndependentResolutionObservation> independentlyLabeledFrame,
        IReadOnlyCollection<string> preBlockingBaseIdentityFingerprints,
        Guid recordedRunId,
        Guid recordedModelId,
        string recordedModelVersion,
        string recordedRuleSetFingerprintSha256,
        decimal threshold,
        decimal conflictMargin,
        bool completeSqlCandidateUniverse,
        int maximumAcceptedLinksPerBaseIdentity,
        double alpha = 0.05)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(recordedRunnerObservations);
        ArgumentNullException.ThrowIfNull(independentlyLabeledFrame);
        ArgumentNullException.ThrowIfNull(preBlockingBaseIdentityFingerprints);
        if (string.IsNullOrWhiteSpace(recordedModelVersion)
            || !string.Equals(recordedModelVersion, manifest.Evaluation.ModelVersion, StringComparison.Ordinal)
            || !string.Equals(recordedRuleSetFingerprintSha256,
                manifest.RuleSetFingerprintSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Versões do Runner divergem dos snapshots de avaliação fixados.");

        // This is the same data path required for a genuine recorded HML
        // run, but the caller must still authenticate HML provenance and
        // attest non-overlap with TRAIN and the still-sealed TEST corpus.
        var binding = V9RecordedRunnerOutcomeBridge.Bind(
            recordedRunId, recordedModelId,
            recordedRunnerObservations, independentlyLabeledFrame,
            completeSqlCandidateUniverse);
        var validation = V9RecordedClusterFdrValidation.Evaluate(
            manifest, independentlyLabeledFrame, binding.RecordedOutcomes,
            preBlockingBaseIdentityFingerprints, binding.BaseIdentityBindings,
            binding.RunId, recordedModelVersion,
            recordedRuleSetFingerprintSha256, threshold, conflictMargin,
            completeSqlCandidateUniverse, maximumAcceptedLinksPerBaseIdentity, alpha);
        if (validation.OperationallyCertified || binding.OperationallyCertified
            || validation.PreBlockingBaseIdentities != preBlockingBaseIdentityFingerprints.Count)
            throw new InvalidDataException("Preview V9 não pode declarar certificação operacional.");

        var evidence = string.Join("|",
            Version, "VALIDATION", binding.Version,
            binding.RunId.ToString("D"),
            binding.ModelId.ToString("D"),
            recordedModelVersion, recordedRuleSetFingerprintSha256,
            validation.RecordedEvaluationFingerprintSha256,
            validation.BoundCohortEvidenceFingerprintSha256);
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(evidence))).ToLowerInvariant();
        return new V9RecordedRunnerBoundEvaluationReport(
            Version, "VALIDATION", binding.RunId, binding.ModelId,
            validation.PreBlockingBaseIdentities,
            binding.RecordedOutcomes.Count, fingerprint,
            validation, OperationallyCertified: false);
    }
}
