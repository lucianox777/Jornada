using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Observed independent-group support per exclusive joint nominal cell.
/// Counts are groups, not pairs or design-weight ESS. No identifiers leave
/// this aggregate; no cell-level independence or precision is certified.
/// </summary>
public sealed record CandidateJointGroupSupportCellV9(
    CompositeNominalLevelV9 Person,
    CompositeNominalLevelV9 Mother,
    int TrainingMatchedGroups,
    int TrainingUnmatchedGroups,
    int EvaluationMatchedGroups,
    int EvaluationUnmatchedGroups,
    bool TrainingHasMinimumGroupsInBothClasses,
    bool EvaluationHasMinimumGroupsInBothClasses);

/// <summary>
/// Descriptive holdout diagnostic for empirically distinct independence groups.
/// Values are grounded in the same raw evidence and partitioned candidate
/// corpus as the V9 weighted 81-cell holdout, never in smoothing pseudocounts.
/// </summary>
public sealed record CandidateJointGroupSupportAuditV9(
    string Version,
    string Status,
    string FrameFingerprint,
    string SelectionFingerprint,
    int MinimumIndependentGroupsPerClassAndCell,
    int TrainingCellsWithMinimumGroupsInBothClasses,
    int EvaluationCellsWithMinimumGroupsInBothClasses,
    int CellsWithMinimumGroupsInBothPartitions,
    IReadOnlyList<CandidateJointGroupSupportCellV9> Cells);

public static class CandidateJointGroupSupportAuditBuilderV9
{
    public const string Version = "V9_JOINT_INDEPENDENT_GROUP_SUPPORT_AUDIT_1";
    public const string Status =
        "DIAGNOSTIC_ONLY_OBSERVED_GROUP_COUNTS_NOT_CELL_CI_OR_SQL_CERTIFICATION";

    public static CandidateJointGroupSupportAuditV9 Analyze(
        ValidatedCandidateCorpus corpus,
        IReadOnlyList<CandidateNominalV9RawEvidence> rawEvidence,
        CandidateWeightedJointHoldoutAuditV9 holdout,
        int minimumIndependentGroupsPerClassAndCell = 2)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(rawEvidence);
        ArgumentNullException.ThrowIfNull(holdout);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumIndependentGroupsPerClassAndCell);

        // Strictly validate the supplied aggregates before comparing them to
        // the independently re-extracted, complete restricted pair inventory.
        var observed = CandidateJointSupportAuditBuilderV9.Analyze(holdout);
        var levels = CandidateNominalV9EvidenceExtractor.Extract(corpus, rawEvidence);
        if (observed.FrameFingerprint != corpus.Capture.FrameFingerprint
            || observed.SelectionFingerprint != corpus.Capture.SelectionFingerprint
            || levels.Count != corpus.Observations.Count)
            throw new InvalidOperationException("Grupos V9 não correspondem ao frame/partição do holdout.");

        var groups = new Dictionary<
            (CandidateCorpusPartition Partition, IndependentMatchLabel Label,
             CompositeNominalLevelV9 Person, CompositeNominalLevelV9 Mother),
            HashSet<Guid>>();
        var counts = new Dictionary<
            (CandidateCorpusPartition Partition, IndependentMatchLabel Label,
             CompositeNominalLevelV9 Person, CompositeNominalLevelV9 Mother), int>();
        var trainIds = new HashSet<Guid>();
        var evaluationIds = new HashSet<Guid>();
        var fullClassGroups = new Dictionary<
            (CandidateCorpusPartition Partition, IndependentMatchLabel Label), HashSet<Guid>>();

        for (var i = 0; i < levels.Count; i++)
        {
            var row = corpus.Observations[i];
            if (row is null || row.Sample is null
                || row.IndependenceGroupId == Guid.Empty
                || row.Label is not (IndependentMatchLabel.Match or IndependentMatchLabel.NonMatch)
                || row.Partition is not (CandidateCorpusPartition.Training or CandidateCorpusPartition.Evaluation))
                throw new InvalidOperationException("Grupos V9 exigem pares rotulados, completos e particionados.");

            (row.Partition == CandidateCorpusPartition.Training
                ? trainIds : evaluationIds).Add(row.IndependenceGroupId);
            if (!fullClassGroups.TryGetValue((row.Partition, row.Label), out var allGroups))
                fullClassGroups[(row.Partition, row.Label)] = allGroups = new HashSet<Guid>();
            allGroups.Add(row.IndependenceGroupId);

            var key = (row.Partition, row.Label, levels[i].Person, levels[i].Mother);
            if (!groups.TryGetValue(key, out var cellGroups))
                groups[key] = cellGroups = new HashSet<Guid>();
            cellGroups.Add(row.IndependenceGroupId);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        if (trainIds.Overlaps(evaluationIds)
            || fullClassGroups.GetValueOrDefault((CandidateCorpusPartition.Training,
                IndependentMatchLabel.Match))?.Count != observed.IndependentTrainingMatchedGroups
            || fullClassGroups.GetValueOrDefault((CandidateCorpusPartition.Training,
                IndependentMatchLabel.NonMatch))?.Count != observed.IndependentTrainingUnmatchedGroups
            || fullClassGroups.GetValueOrDefault((CandidateCorpusPartition.Evaluation,
                IndependentMatchLabel.Match))?.Count != observed.IndependentEvaluationMatchedGroups
            || fullClassGroups.GetValueOrDefault((CandidateCorpusPartition.Evaluation,
                IndependentMatchLabel.NonMatch))?.Count != observed.IndependentEvaluationUnmatchedGroups)
            throw new InvalidOperationException("Grupo de independência duplicado entre partições ou classe divergente.");

        int trainBoth = 0, evaluationBoth = 0, all = 0;
        var cells = new List<CandidateJointGroupSupportCellV9>(observed.Cells.Count);
        foreach (var cell in observed.Cells)
        {
            int Count(CandidateCorpusPartition part, IndependentMatchLabel label) =>
                counts.GetValueOrDefault((part, label, cell.Person, cell.Mother));
            int Distinct(CandidateCorpusPartition part, IndependentMatchLabel label) =>
                groups.TryGetValue((part, label, cell.Person, cell.Mother), out var ids)
                    ? ids.Count : 0;

            // Recompute all four cell-pair counts from raw V9 levels,
            // rejecting even internally consistent but unrelated sidecars.
            if (Count(CandidateCorpusPartition.Training, IndependentMatchLabel.Match) != cell.TrainingMatchedPairs
                || Count(CandidateCorpusPartition.Training, IndependentMatchLabel.NonMatch) != cell.TrainingUnmatchedPairs
                || Count(CandidateCorpusPartition.Evaluation, IndependentMatchLabel.Match) != cell.EvaluationMatchedPairs
                || Count(CandidateCorpusPartition.Evaluation, IndependentMatchLabel.NonMatch) != cell.EvaluationUnmatchedPairs)
                throw new InvalidOperationException("Contagens por célula divergem da evidência nominal recalculada.");

            var tm = Distinct(CandidateCorpusPartition.Training, IndependentMatchLabel.Match);
            var tu = Distinct(CandidateCorpusPartition.Training, IndependentMatchLabel.NonMatch);
            var em = Distinct(CandidateCorpusPartition.Evaluation, IndependentMatchLabel.Match);
            var eu = Distinct(CandidateCorpusPartition.Evaluation, IndependentMatchLabel.NonMatch);
            var sufficientTrain = tm >= minimumIndependentGroupsPerClassAndCell
                && tu >= minimumIndependentGroupsPerClassAndCell;
            var sufficientEvaluation = em >= minimumIndependentGroupsPerClassAndCell
                && eu >= minimumIndependentGroupsPerClassAndCell;
            if (sufficientTrain) trainBoth++;
            if (sufficientEvaluation) evaluationBoth++;
            if (sufficientTrain && sufficientEvaluation) all++;
            cells.Add(new CandidateJointGroupSupportCellV9(
                cell.Person, cell.Mother, tm, tu, em, eu,
                sufficientTrain, sufficientEvaluation));
        }

        return new CandidateJointGroupSupportAuditV9(
            Version, Status, observed.FrameFingerprint, observed.SelectionFingerprint,
            minimumIndependentGroupsPerClassAndCell, trainBoth, evaluationBoth, all,
            Array.AsReadOnly(cells.ToArray()));
    }
}
