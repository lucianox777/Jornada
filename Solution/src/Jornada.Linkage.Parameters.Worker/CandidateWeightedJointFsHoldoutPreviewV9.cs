using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Aggregate confusion matrix in the independently partitioned EVALUATION
/// portion of the candidate-SRS BirthBlockingPlan research corpus.
/// It is NOT operational FDR/recall, an untouched TEST set or certification.
/// </summary>
public sealed record CandidateWeightedJointFsHoldoutOutcomeV9(
    string Version,
    string Status,
    string FrameFingerprint,
    string SelectionFingerprint,
    string TrainingDiagnosticVersion,
    string NominalContractVersion,
    decimal DecisionThreshold,
    int EvaluationPairs,
    int MatchedEvaluationGroups,
    int UnmatchedEvaluationGroups,
    decimal MatchedEvaluationEffectiveSampleSize,
    decimal UnmatchedEvaluationEffectiveSampleSize,
    int TruePositivePairs,
    int FalsePositivePairs,
    int FalseNegativePairs,
    int TrueNegativePairs,
    decimal TruePositiveDesignWeight,
    decimal FalsePositiveDesignWeight,
    decimal FalseNegativeDesignWeight,
    decimal TrueNegativeDesignWeight,
    decimal? ObservedWeightedFalseDiscoveryShare,
    decimal ObservedWeightedRecallShare);

/// <summary>
/// Bridges recomputed, independently labeled TRAIN 81-cell M/U diagnostics
/// to the *existing* C# FS scorer on the separate EVALUATION partition.
/// Threshold is supplied by the caller; it is never selected/optimized here.
/// Never export pair IDs, names, scores, probabilities or individual labels.
/// </summary>
public static class CandidateWeightedJointFsHoldoutPreviewV9
{
    public const string Version = "V9_WEIGHTED_FS_INDEPENDENT_HOLDOUT_PREVIEW_1";
    public const string Status =
        "DIAGNOSTIC_ONLY_BIRTH_BLOCKING_EVALUATION_NOT_OPERATIONAL_TEST_FDR";

    public static CandidateWeightedJointFsHoldoutOutcomeV9 Analyze(
        ValidatedCandidateCorpus corpus,
        IReadOnlyList<CandidateNominalV9RawEvidence> rawEvidence,
        IReadOnlyDictionary<string, decimal> fsParameters,
        decimal decisionThreshold,
        decimal smoothingAlpha,
        decimal minimumEffectiveSampleSize = 2m)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(rawEvidence);
        ArgumentNullException.ThrowIfNull(fsParameters);
        if (decisionThreshold <= 0m || decisionThreshold >= 1m)
            throw new ArgumentOutOfRangeException(nameof(decisionThreshold));

        // One governed raw-evidence inventory and isolated groups. TRAIN
        // determines 81 m/u values; EVALUATION is read only AFTER training.
        // This also fails closed for low class ESS and inadequate eval groups.
        var audit = CandidateWeightedJointHoldoutAuditBuilderV9.AnalyzeFromRawEvidence(
            corpus, rawEvidence, smoothingAlpha, minimumEffectiveSampleSize);
        var training = audit.Training;
        var cells = CandidateWeightedJointPreviewAdapterV9.FromTrainingDiagnostic(training);
        var rawByKey = rawEvidence.ToDictionary(x => (x.SourceId, x.CandidateId));
        decimal tpWeight = 0m, fpWeight = 0m, fnWeight = 0m, tnWeight = 0m;
        int tp = 0, fp = 0, fn = 0, tn = 0, evaluated = 0;

        foreach (var observation in corpus.Observations)
        {
            if (observation.Partition != CandidateCorpusPartition.Evaluation)
                continue;
            if (observation.Label is not (IndependentMatchLabel.Match or IndependentMatchLabel.NonMatch))
                throw new InvalidOperationException("Rótulo de avaliação V9 deve ser independente e conclusivo.");

            var pair = rawByKey[(observation.Sample.SourceId, observation.Sample.CandidateId)].Values;
            var result = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                fsParameters, pair.LeftName, pair.RightName,
                pair.LeftMother, pair.RightMother,
                cells, CompositeNominalComparisonV9.ContractVersion,
                leftBirthDate: pair.LeftBirthDate,
                rightBirthDate: pair.RightBirthDate);
            var predicted = result.Score.Posterior >= decisionThreshold;
            var actual = observation.Label == IndependentMatchLabel.Match;
            var weight = observation.Sample.DesignWeight;
            if (predicted && actual) { tp++; tpWeight += weight; }
            else if (predicted) { fp++; fpWeight += weight; }
            else if (actual) { fn++; fnWeight += weight; }
            else { tn++; tnWeight += weight; }
            evaluated++;
        }

        if (evaluated != audit.MatchedEvaluation.EvaluationPairCount
                + audit.UnmatchedEvaluation.EvaluationPairCount
            || tp + fn != audit.MatchedEvaluation.EvaluationPairCount
            || fp + tn != audit.UnmatchedEvaluation.EvaluationPairCount
            || tpWeight + fnWeight != audit.MatchedEvaluation.TotalDesignWeight
            || fpWeight + tnWeight != audit.UnmatchedEvaluation.TotalDesignWeight)
            throw new InvalidOperationException("Auditoria FS V9 não conciliou a avaliação independente.");

        var positiveWeight = tpWeight + fpWeight;
        var actualMatchWeight = tpWeight + fnWeight;
        return new CandidateWeightedJointFsHoldoutOutcomeV9(
            Version, Status, training.FrameFingerprint, training.SelectionFingerprint,
            training.Version, training.NominalContractVersion, decisionThreshold, evaluated,
            audit.MatchedEvaluation.IndependentEvaluationGroups,
            audit.UnmatchedEvaluation.IndependentEvaluationGroups,
            audit.MatchedEvaluation.KishEffectiveSampleSize,
            audit.UnmatchedEvaluation.KishEffectiveSampleSize,
            tp, fp, fn, tn, tpWeight, fpWeight, fnWeight, tnWeight,
            positiveWeight == 0m ? null : fpWeight / positiveWeight,
            tpWeight / actualMatchWeight);
    }
}
