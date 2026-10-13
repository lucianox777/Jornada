using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Aggregate observed support per joint person/mother state. None of these
/// statuses certify effective independence inside a cell, population validity,
/// a confidence interval, or operational algorithm activation.
/// </summary>
public sealed record CandidateJointSupportCellV9(
    CompositeNominalLevelV9 Person,
    CompositeNominalLevelV9 Mother,
    int TrainingMatchedPairs,
    int TrainingUnmatchedPairs,
    int EvaluationMatchedPairs,
    int EvaluationUnmatchedPairs,
    decimal TrainingMatchedWeight,
    decimal TrainingUnmatchedWeight,
    decimal EvaluationMatchedWeight,
    decimal EvaluationUnmatchedWeight,
    bool HasDeclaredTrainingSupport,
    bool HasDeclaredEvaluationSupport);

/// <summary>
/// Observed M/U support in BOTH TRAIN and independently partitioned EVALUATION.
/// Threshold counts PAIRS, not independent people. The group counts below
/// are global per class, not per-cell; "observed" never means certified.
/// This report cannot assert that any particular 30,000-person corpus ran.
/// </summary>
public sealed record CandidateJointSupportAuditV9(
    string Version,
    string Status,
    string FrameFingerprint,
    string SelectionFingerprint,
    string TrainingDiagnosticVersion,
    int MinimumObservedPairsPerClass,
    int JointCellCount,
    int TrainingCellsWithObservedBothClasses,
    int EvaluationCellsWithObservedBothClasses,
    int CellsWithObservedSupportInBothPartitions,
    int TrainingCellsWithZeroMatchedPairs,
    int TrainingCellsWithZeroUnmatchedPairs,
    int TrainingMatchedPairs,
    int TrainingUnmatchedPairs,
    int EvaluationMatchedPairs,
    int EvaluationUnmatchedPairs,
    int IndependentTrainingMatchedGroups,
    int IndependentTrainingUnmatchedGroups,
    int IndependentEvaluationMatchedGroups,
    int IndependentEvaluationUnmatchedGroups,
    decimal TrainingMatchedEffectiveSampleSize,
    decimal TrainingUnmatchedEffectiveSampleSize,
    decimal EvaluationMatchedEffectiveSampleSize,
    decimal EvaluationUnmatchedEffectiveSampleSize,
    decimal TrainingMatchedWeightShareInUnsupportedCells,
    decimal TrainingUnmatchedWeightShareInUnsupportedCells,
    decimal EvaluationMatchedWeightShareInUnsupportedTrainingCells,
    decimal EvaluationUnmatchedWeightShareInUnsupportedTrainingCells,
    IReadOnlyList<CandidateJointSupportCellV9> Cells);

/// <summary>
/// Quantifies sparsity of an ALREADY GENERATED V9 weighted joint holdout.
/// Consumes only validated aggregate diagnostics. It never fits m/u, selects
/// thresholds, or uses evaluation labels to refit TRAIN. It is applicable
/// to research samples of any size, not evidence of a 30k execution by itself.
/// </summary>
public static class CandidateJointSupportAuditBuilderV9
{
    public const string Version = "V9_JOINT_OBSERVED_SUPPORT_AUDIT_1";
    public const string Status =
        "DIAGNOSTIC_ONLY_PAIR_SUPPORT_NOT_INDEPENDENT_CELL_OR_SQL_CERTIFICATION";

    public static CandidateJointSupportAuditV9 Analyze(
        CandidateWeightedJointHoldoutAuditV9 audit,
        int minimumObservedPairsPerClass = 1)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumObservedPairsPerClass);
        if (audit.Version != CandidateWeightedJointHoldoutAuditBuilderV9.Version
            || audit.Status != CandidateWeightedJointHoldoutAuditBuilderV9.Status)
            throw new InvalidOperationException("Auditoria de suporte V9 exige holdout conjunto versionado.");

        // Reject malformed TRAIN aggregates before taking denominators,
        // including probability tables that sum to 1 only because of smoothing.
        _ = CandidateWeightedJointPreviewAdapterV9.FromTrainingDiagnostic(audit.Training);
        var train = audit.Training;
        var matched = audit.MatchedEvaluation;
        var unmatched = audit.UnmatchedEvaluation;
        if (matched is null || unmatched is null
            || matched.Label != IndependentMatchLabel.Match
            || unmatched.Label != IndependentMatchLabel.NonMatch
            || matched.EvaluationPairCount <= 0 || unmatched.EvaluationPairCount <= 0
            || matched.IndependentEvaluationGroups < 2
            || unmatched.IndependentEvaluationGroups < 2
            || matched.KishEffectiveSampleSize < 1m
            || unmatched.KishEffectiveSampleSize < 1m
            || matched.TotalDesignWeight <= 0m
            || unmatched.TotalDesignWeight <= 0m)
            throw new InvalidOperationException("Holdout V9 tem classes EVALUATION inválidas.");

        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        var expected = levels.Length * levels.Length;
        if (audit.EvaluationCells is null || audit.EvaluationCells.Count != expected)
            throw new InvalidOperationException("Holdout V9 exige exatamente 81 células observadas.");

        var evaluation = new Dictionary<
            (CompositeNominalLevelV9 Person, CompositeNominalLevelV9 Mother),
            CandidateJointHoldoutCellV9>();
        foreach (var cell in audit.EvaluationCells)
        {
            if (cell is null || !Enum.IsDefined(cell.Person) || !Enum.IsDefined(cell.Mother)
                || cell.MatchedObservedPairs < 0 || cell.UnmatchedObservedPairs < 0
                || cell.MatchedObservedWeight < 0m || cell.UnmatchedObservedWeight < 0m
                || (cell.MatchedObservedPairs == 0) != (cell.MatchedObservedWeight == 0m)
                || (cell.UnmatchedObservedPairs == 0) != (cell.UnmatchedObservedWeight == 0m)
                || !evaluation.TryAdd((cell.Person, cell.Mother), cell))
                throw new InvalidOperationException("Célula EVALUATION inválida ou duplicada.");
        }

        if (evaluation.Values.Sum(x => (long)x.MatchedObservedPairs) != matched.EvaluationPairCount
            || evaluation.Values.Sum(x => (long)x.UnmatchedObservedPairs) != unmatched.EvaluationPairCount
            || Math.Abs(evaluation.Values.Sum(x => x.MatchedObservedWeight) - matched.TotalDesignWeight) > .000001m
            || Math.Abs(evaluation.Values.Sum(x => x.UnmatchedObservedWeight) - unmatched.TotalDesignWeight) > .000001m)
            throw new InvalidOperationException("Suporte EVALUATION diverge dos totais ponderados por classe.");

        int trainBoth = 0, evalBoth = 0, bothPartitions = 0;
        int trainZeroM = 0, trainZeroU = 0;
        decimal trainUnsupportedM = 0m, trainUnsupportedU = 0m;
        decimal evalUnseenM = 0m, evalUnseenU = 0m;
        var cells = new List<CandidateJointSupportCellV9>(expected);

        foreach (var trainingCell in train.Cells)
        {
            var held = evaluation[(trainingCell.Person, trainingCell.Mother)];
            var hasTrain = trainingCell.MatchedObservedPairs >= minimumObservedPairsPerClass
                && trainingCell.UnmatchedObservedPairs >= minimumObservedPairsPerClass;
            var hasEval = held.MatchedObservedPairs >= minimumObservedPairsPerClass
                && held.UnmatchedObservedPairs >= minimumObservedPairsPerClass;
            if (hasTrain) trainBoth++;
            if (hasEval) evalBoth++;
            if (hasTrain && hasEval) bothPartitions++;
            if (trainingCell.MatchedObservedPairs == 0) trainZeroM++;
            if (trainingCell.UnmatchedObservedPairs == 0) trainZeroU++;
            if (!hasTrain)
            {
                trainUnsupportedM += trainingCell.MatchedObservedWeight;
                trainUnsupportedU += trainingCell.UnmatchedObservedWeight;
                evalUnseenM += held.MatchedObservedWeight;
                evalUnseenU += held.UnmatchedObservedWeight;
            }
            cells.Add(new CandidateJointSupportCellV9(
                trainingCell.Person, trainingCell.Mother,
                trainingCell.MatchedObservedPairs, trainingCell.UnmatchedObservedPairs,
                held.MatchedObservedPairs, held.UnmatchedObservedPairs,
                trainingCell.MatchedObservedWeight, trainingCell.UnmatchedObservedWeight,
                held.MatchedObservedWeight, held.UnmatchedObservedWeight,
                hasTrain, hasEval));
        }

        // This is a TRAIN-derived description. No tuning or re-estimation
        // occurs using the held-out EVALUATION partition.
        return new CandidateJointSupportAuditV9(
            Version, Status, train.FrameFingerprint, train.SelectionFingerprint,
            train.Version, minimumObservedPairsPerClass, expected,
            trainBoth, evalBoth, bothPartitions, trainZeroM, trainZeroU,
            train.Matched.TrainingPairCount, train.Unmatched.TrainingPairCount,
            matched.EvaluationPairCount, unmatched.EvaluationPairCount,
            train.Matched.IndependentTrainingGroups,
            train.Unmatched.IndependentTrainingGroups,
            matched.IndependentEvaluationGroups,
            unmatched.IndependentEvaluationGroups,
            train.Matched.EffectiveSampleSize, train.Unmatched.EffectiveSampleSize,
            matched.KishEffectiveSampleSize, unmatched.KishEffectiveSampleSize,
            trainUnsupportedM / train.Matched.TotalDesignWeight,
            trainUnsupportedU / train.Unmatched.TotalDesignWeight,
            evalUnseenM / matched.TotalDesignWeight,
            evalUnseenU / unmatched.TotalDesignWeight,
            Array.AsReadOnly(cells.ToArray()));
    }
}
