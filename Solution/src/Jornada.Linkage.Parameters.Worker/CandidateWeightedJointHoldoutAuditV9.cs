using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Aggregate evaluation-only class profile. Kish ESS accounts for unequal
/// pair weights, not clustering by identity or missing population strata.
/// Total variation is an observed class-conditional distribution difference,
/// NOT a p-value, confidence interval, model-selection criterion or FDR.
/// </summary>
public sealed record CandidateJointHoldoutClassV9(
    IndependentMatchLabel Label,
    int EvaluationPairCount,
    int IndependentEvaluationGroups,
    decimal TotalDesignWeight,
    decimal KishEffectiveSampleSize,
    decimal TotalVariationFromTrainingObserved,
    decimal EvaluationWeightInUnseenTrainingCells,
    CandidateJointHoldoutGroupSensitivityV9 GroupSensitivity);

/// <summary>
/// Deterministic leave-one-independent-group-out influence, not a confidence
/// interval. No group identity or per-group output is retained in this DTO.
/// At least 3 groups are needed to leave >=2 independent groups after deletion.
/// </summary>
public sealed record CandidateJointHoldoutGroupSensitivityV9(
    string Status,
    int IndependentGroups,
    int RemovalsEvaluated,
    decimal? MinimumLeaveOneGroupOutTvd,
    decimal? MaximumLeaveOneGroupOutTvd,
    decimal? MaximumAbsoluteTvdChange,
    decimal? MaximumUnseenTrainingWeightShare);

public sealed record CandidateJointHoldoutCellV9(
    CompositeNominalLevelV9 Person,
    CompositeNominalLevelV9 Mother,
    int MatchedObservedPairs,
    int UnmatchedObservedPairs,
    decimal MatchedObservedWeight,
    decimal UnmatchedObservedWeight);

/// <summary>
/// Immutable aggregate-only audit; no raw names, pair IDs, group IDs or
/// per-pair labels/weights. The returned training object remains diagnostic.
/// </summary>
public sealed record CandidateWeightedJointHoldoutAuditV9(
    string Version,
    string Status,
    CandidateWeightedJointDiagnosticV9 Training,
    CandidateJointHoldoutClassV9 MatchedEvaluation,
    CandidateJointHoldoutClassV9 UnmatchedEvaluation,
    IReadOnlyList<CandidateJointHoldoutCellV9> EvaluationCells);

/// <summary>
/// Independently held-out EVALUATION of 81 V9 nominal states on the same
/// candidate-SRS frame used by CandidateWeightedJointNominalEstimatorV9.
/// It never fits parameters, tunes thresholds, or authorizes runtime scoring.
/// BirthBlockingPlan != temporal SQL VERSIONED_ALIAS candidate universe.
/// </summary>
public static class CandidateWeightedJointHoldoutAuditBuilderV9
{
    public const string Version = "V9_WEIGHTED_JOINT_INDEPENDENT_HOLDOUT_AUDIT_1";
    public const string Status = "DIAGNOSTIC_ONLY_EVALUATION_BIRTH_BLOCKING_NOT_SQL_CERTIFIED";
    public const string InsufficientGroupsStatus = "UNAVAILABLE_LT_3_INDEPENDENT_EVALUATION_GROUPS";
    public const string SensitivityStatus = "DIAGNOSTIC_ONLY_GROUP_INFLUENCE_NOT_CI";

    public static CandidateWeightedJointHoldoutAuditV9 AnalyzeFromRawEvidence(
        ValidatedCandidateCorpus corpus,
        IReadOnlyList<CandidateNominalV9RawEvidence> rawEvidence,
        decimal smoothingAlpha,
        decimal minimumEffectiveSampleSize = 2m)
    {
        // The existing extractor proves one-to-one evidence coverage and
        // recomputes both levels from the V9 comparator. The existing
        // estimator checks frame, sampling design, labels, and group leakage,
        // and fits M/U on TRAIN only.
        var derived = CandidateNominalV9EvidenceExtractor.Extract(corpus, rawEvidence);
        var training = CandidateWeightedJointNominalEstimatorV9.Estimate(
            corpus, derived, smoothingAlpha, minimumEffectiveSampleSize)
            with { EvidenceGeneration = CandidateNominalV9EvidenceExtractor.Version };

        var byPair = derived.ToDictionary(x => (x.SourceId, x.CandidateId));
        var observations = corpus.Observations
            .Where(x => x.Partition == CandidateCorpusPartition.Evaluation)
            .ToArray();
        var matched = ClassProfile(IndependentMatchLabel.Match);
        var unmatched = ClassProfile(IndependentMatchLabel.NonMatch);

        var cells = new List<CandidateJointHoldoutCellV9>(training.Cells.Count);
        foreach (var cell in training.Cells)
        {
            var key = (cell.Person, cell.Mother);
            var m = matched.Support.GetValueOrDefault(key);
            var u = unmatched.Support.GetValueOrDefault(key);
            cells.Add(new CandidateJointHoldoutCellV9(
                cell.Person, cell.Mother, m.Count, u.Count, m.Weight, u.Weight));
        }
        return new CandidateWeightedJointHoldoutAuditV9(
            Version, Status, training, matched.Summary, unmatched.Summary,
            Array.AsReadOnly(cells.ToArray()));

        (CandidateJointHoldoutClassV9 Summary,
         Dictionary<(CompositeNominalLevelV9, CompositeNominalLevelV9), (int Count, decimal Weight)> Support)
            ClassProfile(IndependentMatchLabel label)
        {
            var selected = observations.Where(x => x.Label == label).ToArray();
            var groups = selected.Select(x => x.IndependenceGroupId).Distinct().Count();
            if (groups < 2 || selected.Length < 2)
                throw new InvalidOperationException(
                    "V9 holdout requer pelo menos dois grupos independentes de EVALUATION por classe.");

            var support = new Dictionary<(CompositeNominalLevelV9, CompositeNominalLevelV9), (int Count, decimal Weight)>();
            // Group contribution dictionaries are transient. Only aggregate
            // extrema leave the function; no group UUIDs appear in output.
            var byGroup = new Dictionary<Guid, Dictionary<(CompositeNominalLevelV9, CompositeNominalLevelV9), decimal>>();
            decimal total = 0m, squared = 0m;
            foreach (var row in selected)
            {
                var w = row.Sample.DesignWeight; // verified by training estimator
                var state = byPair[(row.Sample.SourceId, row.Sample.CandidateId)];
                var key = (state.Person, state.Mother);
                support.TryGetValue(key, out var prior);
                support[key] = (checked(prior.Count + 1), prior.Weight + w);
                if (!byGroup.TryGetValue(row.IndependenceGroupId, out var contribution))
                {
                    contribution = new Dictionary<(CompositeNominalLevelV9, CompositeNominalLevelV9), decimal>();
                    byGroup.Add(row.IndependenceGroupId, contribution);
                }
                contribution[key] = contribution.GetValueOrDefault(key) + w;
                total += w;
                squared += w * w;
            }
            var ess = total * total / squared;
            if (ess < minimumEffectiveSampleSize)
                throw new InvalidOperationException(
                    "V9 holdout tem amostra efetiva insuficiente para essa classe.");

            // Compute TOTAL VARIATION from unsmoothed TRAIN observed weights.
            // Smoothing pseudocounts never count as empirical support.
            var sumAbsolute = 0m;
            var unseenWeight = 0m;
            foreach (var cell in training.Cells)
            {
                var key = (cell.Person, cell.Mother);
                var observed = support.GetValueOrDefault(key).Weight;
                var trainWeight = label == IndependentMatchLabel.Match
                    ? cell.MatchedObservedWeight : cell.UnmatchedObservedWeight;
                var trainTotal = label == IndependentMatchLabel.Match
                    ? training.Matched.TotalDesignWeight : training.Unmatched.TotalDesignWeight;
                sumAbsolute += Math.Abs(observed / total - trainWeight / trainTotal);
                if (trainWeight == 0m) unseenWeight += observed;
            }

            var fullTvd = sumAbsolute / 2m;
            var influence = Sensitivity();
            return (new CandidateJointHoldoutClassV9(
                label, selected.Length, groups, total, ess,
                fullTvd, unseenWeight, influence), support);

            CandidateJointHoldoutGroupSensitivityV9 Sensitivity()
            {
                if (groups < 3)
                    return new CandidateJointHoldoutGroupSensitivityV9(
                        InsufficientGroupsStatus, groups, 0, null, null, null, null);

                var minTvd = decimal.MaxValue;
                var maxTvd = decimal.MinValue;
                var largestChange = 0m;
                var highestUnseenShare = 0m;
                foreach (var contribution in byGroup.Values)
                {
                    var removed = contribution.Values.Sum();
                    var remaining = total - removed;
                    if (remaining <= 0m)
                        throw new InvalidOperationException("V9 holdout: remoção de grupo esvaziou a classe.");
                    var difference = 0m;
                    var unseen = 0m;
                    foreach (var cell in training.Cells)
                    {
                        var key = (cell.Person, cell.Mother);
                        var observed = support.GetValueOrDefault(key).Weight
                            - contribution.GetValueOrDefault(key);
                        var trainWeight = label == IndependentMatchLabel.Match
                            ? cell.MatchedObservedWeight : cell.UnmatchedObservedWeight;
                        var trainTotal = label == IndependentMatchLabel.Match
                            ? training.Matched.TotalDesignWeight : training.Unmatched.TotalDesignWeight;
                        difference += Math.Abs(observed / remaining - trainWeight / trainTotal);
                        if (trainWeight == 0m) unseen += observed;
                    }
                    var tvd = difference / 2m;
                    minTvd = Math.Min(minTvd, tvd);
                    maxTvd = Math.Max(maxTvd, tvd);
                    largestChange = Math.Max(largestChange, Math.Abs(tvd - fullTvd));
                    highestUnseenShare = Math.Max(highestUnseenShare, unseen / remaining);
                }
                return new CandidateJointHoldoutGroupSensitivityV9(
                    SensitivityStatus, groups, byGroup.Count, minTvd, maxTvd,
                    largestChange, highestUnseenShare);
            }
        }
    }
}
