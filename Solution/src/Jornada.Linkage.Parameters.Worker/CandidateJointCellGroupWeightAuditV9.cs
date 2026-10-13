using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Diagnostic Kish ESS after first aggregating candidate design weights by
/// IndependenceGroupId INSIDE one (partition, label, person, mother) cell.
/// Not a confidence interval, population ESS, or proof of group independence.
/// </summary>
public sealed record CandidateJointCellGroupWeightCellV9(
    CompositeNominalLevelV9 Person,
    CompositeNominalLevelV9 Mother,
    decimal? TrainingMatchedGroupWeightEss,
    decimal? TrainingUnmatchedGroupWeightEss,
    decimal? EvaluationMatchedGroupWeightEss,
    decimal? EvaluationUnmatchedGroupWeightEss);

/// <summary>
/// Design-weighted mass in nominal cells without BOTH M and U class-specific
/// independent-group Kish ESS >= 2. All denominators are class-local and
/// partition-local. A high share is a diagnostic, NOT estimated FDR.
/// </summary>
public sealed record CandidateJointCellGroupWeightExposureV9(
    decimal TrainingMatchedWeightShareWithoutTrainBothClassEss,
    decimal TrainingUnmatchedWeightShareWithoutTrainBothClassEss,
    decimal EvaluationMatchedWeightShareWithoutTrainBothClassEss,
    decimal EvaluationUnmatchedWeightShareWithoutTrainBothClassEss,
    decimal EvaluationMatchedWeightShareWithoutEvaluationBothClassEss,
    decimal EvaluationUnmatchedWeightShareWithoutEvaluationBothClassEss);

public sealed record CandidateJointCellGroupWeightAuditV9(
    string Version,
    string Status,
    string FrameFingerprint,
    string SelectionFingerprint,
    int JointCellCount,
    int TrainingCellsWithGroupWeightEssAtLeastTwoInBothClasses,
    int EvaluationCellsWithGroupWeightEssAtLeastTwoInBothClasses,
    int CellsWithGroupWeightEssAtLeastTwoInBothPartitions,
    CandidateJointCellGroupWeightExposureV9 UnsupportedDesignWeightExposure,
    IReadOnlyList<CandidateJointCellGroupWeightCellV9> Cells);

/// <summary>
/// Computes cell-level GROUP-weight ESS (not pair-weight ESS) on the SAME
/// restricted candidate-SRS population as the published 81-cell holdout.
/// TRAIN and EVALUATION never share group IDs. Evaluation weights may only
/// DESCRIBE support; no fitting or model selection is done here.
/// </summary>
public static class CandidateJointCellGroupWeightAuditBuilderV9
{
    public const string Version = "V9_JOINT_CELL_GROUP_WEIGHT_ESS_DIAGNOSTIC_2";
    public const string Status =
        "DIAGNOSTIC_ONLY_GROUP_WEIGHT_KISH_NOT_INDEPENDENCE_CI_OR_SQL_CERTIFICATION";

    public static CandidateJointCellGroupWeightAuditV9 Analyze(
        ValidatedCandidateCorpus corpus,
        IReadOnlyList<CandidateNominalV9RawEvidence> rawEvidence,
        CandidateWeightedJointHoldoutAuditV9 holdout)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(rawEvidence);
        ArgumentNullException.ThrowIfNull(holdout);

        // Re-evaluate raw levels and group counts against the frozen
        // holdout. This rejects wrong inventories, cross-partition group
        // leakage, missing/duplicated evidence, forged per-cell pair counts.
        var groups = CandidateJointGroupSupportAuditBuilderV9.Analyze(
            corpus, rawEvidence, holdout);
        var observed = CandidateJointSupportAuditBuilderV9.Analyze(holdout);
        var levels = CandidateNominalV9EvidenceExtractor.Extract(corpus, rawEvidence);
        if (levels.Count != corpus.Observations.Count
            || groups.FrameFingerprint != observed.FrameFingerprint
            || groups.SelectionFingerprint != observed.SelectionFingerprint
            || groups.Cells.Count != observed.Cells.Count)
            throw new InvalidOperationException("Frame de ESS por grupo V9 diverge da amostra conjunta.");

        // Group keys exist only in the transient local dictionary, NEVER in
        // the audit DTO. Multiple pairs within a group add their weights
        // BEFORE squaring the group contribution.
        var groupedWeights = new Dictionary<
            (CandidateCorpusPartition Partition, IndependentMatchLabel Label,
             CompositeNominalLevelV9 Person, CompositeNominalLevelV9 Mother, Guid Group),
            decimal>();

        for (var i = 0; i < corpus.Observations.Count; i++)
        {
            var row = corpus.Observations[i];
            var weight = row.Sample.DesignWeight;
            if (weight <= 0m || row.IndependenceGroupId == Guid.Empty
                || row.Label is not (IndependentMatchLabel.Match or IndependentMatchLabel.NonMatch)
                || row.Partition is not (CandidateCorpusPartition.Training or CandidateCorpusPartition.Evaluation))
                throw new InvalidOperationException("ESS V9 exige pesos positivos e grupos válidos.");

            var state = levels[i];
            var key = (row.Partition, row.Label, state.Person, state.Mother,
                row.IndependenceGroupId);
            groupedWeights[key] = checked(groupedWeights.GetValueOrDefault(key) + weight);
        }

        var perCell = new Dictionary<
            (CandidateCorpusPartition Partition, IndependentMatchLabel Label,
             CompositeNominalLevelV9 Person, CompositeNominalLevelV9 Mother),
            List<decimal>>();
        foreach (var contribution in groupedWeights)
        {
            var group = contribution.Key;
            var key = (group.Partition, group.Label, group.Person, group.Mother);
            if (!perCell.TryGetValue(key, out var weights))
                perCell[key] = weights = [];
            weights.Add(contribution.Value);
        }

        decimal? Ess(CandidateCorpusPartition partition, IndependentMatchLabel label,
            CandidateJointSupportCellV9 observedCell,
            int expectedGroups, int expectedPairs, decimal expectedWeight)
        {
            var key = (partition, label, observedCell.Person, observedCell.Mother);
            if (!perCell.TryGetValue(key, out var weights))
            {
                if (expectedGroups != 0 || expectedPairs != 0 || expectedWeight != 0m)
                    throw new InvalidOperationException(
                        "Célula V9 sem pesos por grupo diverge do suporte observado.");
                return null; // No empirical support: smoothing is not evidence.
            }

            var sum = 0m;
            var squares = 0m;
            foreach (var weight in weights)
            {
                sum = checked(sum + weight);
                squares = checked(squares + checked(weight * weight));
            }
            if (weights.Count != expectedGroups || expectedGroups > expectedPairs
                || sum != expectedWeight || squares <= 0m)
                throw new InvalidOperationException(
                    "Pesos agregados por grupo não reconciliam com o holdout V9.");

            var ess = checked(sum * sum) / squares;
            // Decimal rounding tolerance is only for division; never treat a
            // group ESS > distinct groups as additional independent evidence.
            if (ess < 1m - 0.000000000000000000000000001m
                || ess > expectedGroups + 0.000000000000000000000000001m)
                throw new InvalidOperationException(
                    "ESS Kish de pesos por grupo fora dos limites da célula.");
            return ess;
        }

        var result = new List<CandidateJointCellGroupWeightCellV9>(observed.Cells.Count);
        int trainBoth = 0, evaluationBoth = 0, all = 0;
        decimal trainingMatchedUnsupported = 0m, trainingUnmatchedUnsupported = 0m;
        decimal evaluationMatchedTrainUnsupported = 0m, evaluationUnmatchedTrainUnsupported = 0m;
        decimal evaluationMatchedSelfUnsupported = 0m, evaluationUnmatchedSelfUnsupported = 0m;
        foreach (var cell in observed.Cells)
        {
            var groupCell = groups.Cells.Single(x =>
                x.Person == cell.Person && x.Mother == cell.Mother);
            var tm = Ess(CandidateCorpusPartition.Training, IndependentMatchLabel.Match,
                cell, groupCell.TrainingMatchedGroups, cell.TrainingMatchedPairs,
                cell.TrainingMatchedWeight);
            var tu = Ess(CandidateCorpusPartition.Training, IndependentMatchLabel.NonMatch,
                cell, groupCell.TrainingUnmatchedGroups, cell.TrainingUnmatchedPairs,
                cell.TrainingUnmatchedWeight);
            var em = Ess(CandidateCorpusPartition.Evaluation, IndependentMatchLabel.Match,
                cell, groupCell.EvaluationMatchedGroups, cell.EvaluationMatchedPairs,
                cell.EvaluationMatchedWeight);
            var eu = Ess(CandidateCorpusPartition.Evaluation, IndependentMatchLabel.NonMatch,
                cell, groupCell.EvaluationUnmatchedGroups, cell.EvaluationUnmatchedPairs,
                cell.EvaluationUnmatchedWeight);
            var training = tm >= 2m && tu >= 2m;
            var evaluation = em >= 2m && eu >= 2m;
            if (training) trainBoth++;
            else
            {
                trainingMatchedUnsupported = checked(trainingMatchedUnsupported + cell.TrainingMatchedWeight);
                trainingUnmatchedUnsupported = checked(trainingUnmatchedUnsupported + cell.TrainingUnmatchedWeight);
                // Independent EVALUATION only DESCRIBES mass in cells
                // unsupported by TRAIN; it cannot adjust TRAIN estimates.
                evaluationMatchedTrainUnsupported =
                    checked(evaluationMatchedTrainUnsupported + cell.EvaluationMatchedWeight);
                evaluationUnmatchedTrainUnsupported =
                    checked(evaluationUnmatchedTrainUnsupported + cell.EvaluationUnmatchedWeight);
            }
            if (evaluation) evaluationBoth++;
            else
            {
                evaluationMatchedSelfUnsupported =
                    checked(evaluationMatchedSelfUnsupported + cell.EvaluationMatchedWeight);
                evaluationUnmatchedSelfUnsupported =
                    checked(evaluationUnmatchedSelfUnsupported + cell.EvaluationUnmatchedWeight);
            }
            if (training && evaluation) all++;
            result.Add(new CandidateJointCellGroupWeightCellV9(
                cell.Person, cell.Mother, tm, tu, em, eu));
        }

        if (result.Count != 81 || trainBoth > groups.TrainingCellsWithMinimumGroupsInBothClasses
            || evaluationBoth > groups.EvaluationCellsWithMinimumGroupsInBothClasses
            || all > groups.CellsWithMinimumGroupsInBothPartitions)
            throw new InvalidOperationException("ESS V9 por grupo excede o suporte de grupos observados.");

        // All class totals must be strictly positive; the governed holdout
        // and independent raw evidence inventories already validated them.
        var trainingMatchedTotal = holdout.Training.Matched.TotalDesignWeight;
        var trainingUnmatchedTotal = holdout.Training.Unmatched.TotalDesignWeight;
        var evaluationMatchedTotal = holdout.MatchedEvaluation.TotalDesignWeight;
        var evaluationUnmatchedTotal = holdout.UnmatchedEvaluation.TotalDesignWeight;
        if (trainingMatchedTotal <= 0m || trainingUnmatchedTotal <= 0m
            || evaluationMatchedTotal <= 0m || evaluationUnmatchedTotal <= 0m)
            throw new InvalidOperationException(
                "Exposição ponderada V9 exige denominadores positivos em todas as classes.");

        var exposure = new CandidateJointCellGroupWeightExposureV9(
            trainingMatchedUnsupported / trainingMatchedTotal,
            trainingUnmatchedUnsupported / trainingUnmatchedTotal,
            evaluationMatchedTrainUnsupported / evaluationMatchedTotal,
            evaluationUnmatchedTrainUnsupported / evaluationUnmatchedTotal,
            evaluationMatchedSelfUnsupported / evaluationMatchedTotal,
            evaluationUnmatchedSelfUnsupported / evaluationUnmatchedTotal);
        var shares = new[]
        {
            exposure.TrainingMatchedWeightShareWithoutTrainBothClassEss,
            exposure.TrainingUnmatchedWeightShareWithoutTrainBothClassEss,
            exposure.EvaluationMatchedWeightShareWithoutTrainBothClassEss,
            exposure.EvaluationUnmatchedWeightShareWithoutTrainBothClassEss,
            exposure.EvaluationMatchedWeightShareWithoutEvaluationBothClassEss,
            exposure.EvaluationUnmatchedWeightShareWithoutEvaluationBothClassEss
        };
        if (shares.Any(share => share < 0m || share > 1m))
            throw new InvalidOperationException(
                "Exposição ponderada sem suporte por grupo fora de [0,1].");

        return new CandidateJointCellGroupWeightAuditV9(
            Version, Status, observed.FrameFingerprint, observed.SelectionFingerprint,
            result.Count, trainBoth, evaluationBoth, all,
            exposure, Array.AsReadOnly(result.ToArray()));
    }
}
