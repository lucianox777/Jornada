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
    decimal EvaluationWeightInUnseenTrainingCells);

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
            decimal total = 0m, squared = 0m;
            foreach (var row in selected)
            {
                var w = row.Sample.DesignWeight; // verified by training estimator
                var state = byPair[(row.Sample.SourceId, row.Sample.CandidateId)];
                var key = (state.Person, state.Mother);
                support.TryGetValue(key, out var prior);
                support[key] = (checked(prior.Count + 1), prior.Weight + w);
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

            return (new CandidateJointHoldoutClassV9(
                label, selected.Length, groups, total, ess,
                sumAbsolute / 2m, unseenWeight), support);
        }
    }
}
