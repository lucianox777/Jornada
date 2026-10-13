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
    decimal ObservedWeightedRecallShare,
    CandidateFsHoldoutGroupSensitivityV9 GroupSensitivity)
{
    /// <summary>Descriptive 81-cell M/U observed support; NOT a certification.</summary>
    public CandidateJointSupportAuditV9? ObservedJointSupport { get; init; }

    /// <summary>
    /// Independently grouped per-cell M/U support recomputed from the full
    /// restricted inventory; NULL only for historical serialized outcomes.
    /// </summary>
    public CandidateJointGroupSupportAuditV9? IndependentJointGroupSupport { get; init; }

    /// <summary>
    /// Descriptive 81-cell Kish ESS AFTER grouping design-weight contributions
    /// by IndependenceGroupId, not the existing pair-weight Kish ESS.
    /// No IDs or group-level records are exported.
    /// </summary>
    public CandidateJointCellGroupWeightAuditV9? IndependentJointCellGroupWeightEss { get; init; }

    /// <summary>
    /// Fixed-threshold EVALUATION predicted-positive and false-positive mass
    /// in TRAIN-unsupported nominal cells, without individual predictions.
    /// Null only in legacy serialized previews.
    /// </summary>
    public CandidateFsUnsupportedTrainingCellDecisionAuditV9? UnsupportedTrainingCellDecisions { get; init; }
}

/// <summary>
/// Fixed-threshold EVALUATION decision mass in nominal cells where TRAIN does
/// not have group-weight ESS >= 2 in BOTH M and U. This is a descriptive
/// sensitivity signal, not a calibrated risk estimate, TEST, or a veto.
/// A null share means no predicted positives, never zero false discoveries.
/// </summary>
public sealed record CandidateFsUnsupportedTrainingCellDecisionAuditV9(
    string Version,
    string Status,
    int PredictedPositivePairsWithoutTrainBilateralEss,
    int TruePositivePairsWithoutTrainBilateralEss,
    int FalsePositivePairsWithoutTrainBilateralEss,
    decimal PredictedPositiveDesignWeightWithoutTrainBilateralEss,
    decimal TruePositiveDesignWeightWithoutTrainBilateralEss,
    decimal FalsePositiveDesignWeightWithoutTrainBilateralEss,
    decimal? PredictedPositiveDesignWeightShareWithoutTrainBilateralEss,
    decimal? ObservedFalseDiscoveryShareWithinUnsupportedPredictions);

/// <summary>
/// Leave-one-independent-EVALUATION-group-out descriptive ranges for the
/// FIXED, caller-supplied threshold. Not a CI or certified FDR. No group IDs
/// or per-group statistics are exposed. A null FDR bound means no positive
/// predictions remained in any eligible deletion.
/// </summary>
public sealed record CandidateFsHoldoutGroupSensitivityV9(
    string Status,
    int IndependentGroups,
    int RemovalsEvaluated,
    decimal? MinimumObservedFalseDiscoveryShare,
    decimal? MaximumObservedFalseDiscoveryShare,
    decimal? MinimumObservedRecallShare,
    decimal? MaximumObservedRecallShare,
    int RemovalsWithNoPredictedPositivePairs);

/// <summary>
/// Bridges recomputed, independently labeled TRAIN 81-cell M/U diagnostics
/// to the *existing* C# FS scorer on the separate EVALUATION partition.
/// Threshold is supplied by the caller; it is never selected/optimized here.
/// Never export pair IDs, names, scores, probabilities or individual labels.
/// </summary>
public static class CandidateWeightedJointFsHoldoutPreviewV9
{
    public const string Version = "V9_WEIGHTED_FS_INDEPENDENT_HOLDOUT_PREVIEW_2";
    public const string Status =
        "DIAGNOSTIC_ONLY_BIRTH_BLOCKING_EVALUATION_NOT_OPERATIONAL_TEST_FDR";
    public const string InsufficientGroupsStatus =
        "UNAVAILABLE_LT_3_INDEPENDENT_EVALUATION_GROUPS";
    public const string SensitivityStatus =
        "DIAGNOSTIC_ONLY_GROUP_INFLUENCE_NOT_CI_OR_CERTIFIED_FDR";

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
        // Attribute the very SAME decisions to the TRAIN support of the
        // observed V9 joint cell. A global 81-cell fraction cannot reveal
        // whether actual predicted matches concentrate in unsupported cells.
        // TRAIN support is frozen before EVALUATION scoring. No threshold is
        // selected here and EVALUATION never changes m/u or cell support.
        var groupWeightAudit = CandidateJointCellGroupWeightAuditBuilderV9.Analyze(
            corpus, rawEvidence, audit);
        var supportByCell = groupWeightAudit.Cells.ToDictionary(x => (x.Person, x.Mother));
        var levelsByPair = CandidateNominalV9EvidenceExtractor.Extract(corpus, rawEvidence)
            .ToDictionary(x => (x.SourceId, x.CandidateId));
        decimal unsupportedTpWeight = 0m, unsupportedFpWeight = 0m;
        int unsupportedTp = 0, unsupportedFp = 0;
        decimal tpWeight = 0m, fpWeight = 0m, fnWeight = 0m, tnWeight = 0m;
        int tp = 0, fp = 0, fn = 0, tn = 0, evaluated = 0;
        // These transient group keys never leave the method or enter the DTO.
        var byGroup = new Dictionary<Guid, (decimal Tp, decimal Fp, decimal Fn)>();

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
            var pairKey = (observation.Sample.SourceId, observation.Sample.CandidateId);
            var state = levelsByPair[pairKey];
            var support = supportByCell[(state.Person, state.Mother)];
            var supportedInTraining = support.TrainingMatchedGroupWeightEss >= 2m
                && support.TrainingUnmatchedGroupWeightEss >= 2m;
            if (predicted && !supportedInTraining)
            {
                if (actual) { unsupportedTp++; unsupportedTpWeight += weight; }
                else { unsupportedFp++; unsupportedFpWeight += weight; }
            }
            if (predicted && actual) { tp++; tpWeight += weight; }
            else if (predicted) { fp++; fpWeight += weight; }
            else if (actual) { fn++; fnWeight += weight; }
            else { tn++; tnWeight += weight; }
            byGroup.TryGetValue(observation.IndependenceGroupId, out var contribution);
            byGroup[observation.IndependenceGroupId] = (
                contribution.Tp + (predicted && actual ? weight : 0m),
                contribution.Fp + (predicted && !actual ? weight : 0m),
                contribution.Fn + (!predicted && actual ? weight : 0m));
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
        var unsupportedPositiveWeight = unsupportedTpWeight + unsupportedFpWeight;
        if (unsupportedTp > tp || unsupportedFp > fp
            || unsupportedTpWeight > tpWeight || unsupportedFpWeight > fpWeight
            || unsupportedPositiveWeight > positiveWeight)
            throw new InvalidOperationException(
                "Diagnóstico V9 de decisões sem suporte TRAIN diverge da matriz FS.");
        var unsupportedDecisions = new CandidateFsUnsupportedTrainingCellDecisionAuditV9(
            "V9_FS_EVALUATION_UNSUPPORTED_TRAIN_CELL_DECISIONS_1",
            "DIAGNOSTIC_ONLY_FIXED_THRESHOLD_EVALUATION_NOT_BLIND_TEST_OR_CERTIFIED_FDR",
            unsupportedTp + unsupportedFp, unsupportedTp, unsupportedFp,
            unsupportedPositiveWeight, unsupportedTpWeight, unsupportedFpWeight,
            positiveWeight == 0m ? null : unsupportedPositiveWeight / positiveWeight,
            unsupportedPositiveWeight == 0m ? null : unsupportedFpWeight / unsupportedPositiveWeight);
        return new CandidateWeightedJointFsHoldoutOutcomeV9(
            Version, Status, training.FrameFingerprint, training.SelectionFingerprint,
            training.Version, training.NominalContractVersion, decisionThreshold, evaluated,
            audit.MatchedEvaluation.IndependentEvaluationGroups,
            audit.UnmatchedEvaluation.IndependentEvaluationGroups,
            audit.MatchedEvaluation.KishEffectiveSampleSize,
            audit.UnmatchedEvaluation.KishEffectiveSampleSize,
            tp, fp, fn, tn, tpWeight, fpWeight, fnWeight, tnWeight,
            positiveWeight == 0m ? null : fpWeight / positiveWeight,
            tpWeight / actualMatchWeight, GroupSensitivity())
        {
            ObservedJointSupport = CandidateJointSupportAuditBuilderV9.Analyze(audit),
            IndependentJointGroupSupport = CandidateJointGroupSupportAuditBuilderV9.Analyze(
                corpus, rawEvidence, audit),
            IndependentJointCellGroupWeightEss = groupWeightAudit,
            UnsupportedTrainingCellDecisions = unsupportedDecisions
        };

        CandidateFsHoldoutGroupSensitivityV9 GroupSensitivity()
        {
            // Each label needs >=3 groups: deleting a group must retain >=2
            // independent groups in BOTH classes, not just in the union.
            if (audit.MatchedEvaluation.IndependentEvaluationGroups < 3
                || audit.UnmatchedEvaluation.IndependentEvaluationGroups < 3)
                return new CandidateFsHoldoutGroupSensitivityV9(
                    InsufficientGroupsStatus, byGroup.Count, 0,
                    null, null, null, null, 0);

            decimal? minFdr = null, maxFdr = null;
            decimal? minRecall = null, maxRecall = null;
            var noPredictions = 0;
            foreach (var group in byGroup.Values)
            {
                var remainingTp = tpWeight - group.Tp;
                var remainingFp = fpWeight - group.Fp;
                var remainingFn = fnWeight - group.Fn;
                var remainingPositives = remainingTp + remainingFp;
                var remainingActualMatches = remainingTp + remainingFn;
                if (remainingActualMatches <= 0m || remainingTp < 0m ||
                    remainingFp < 0m || remainingFn < 0m)
                    throw new InvalidOperationException(
                        "V9 holdout FS sem suporte após exclusão de um grupo.");
                var recall = remainingTp / remainingActualMatches;
                minRecall = minRecall is null ? recall : Math.Min(minRecall.Value, recall);
                maxRecall = maxRecall is null ? recall : Math.Max(maxRecall.Value, recall);
                if (remainingPositives == 0m)
                {
                    noPredictions++;
                    continue;
                }
                var fdr = remainingFp / remainingPositives;
                minFdr = minFdr is null ? fdr : Math.Min(minFdr.Value, fdr);
                maxFdr = maxFdr is null ? fdr : Math.Max(maxFdr.Value, fdr);
            }
            return new CandidateFsHoldoutGroupSensitivityV9(
                SensitivityStatus, byGroup.Count, byGroup.Count,
                minFdr, maxFdr, minRecall, maxRecall, noPredictions);
        }
    }
}
