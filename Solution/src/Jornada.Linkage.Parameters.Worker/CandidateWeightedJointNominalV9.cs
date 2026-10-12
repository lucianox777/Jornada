using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Restricted pair-keyed level sidecar. These identifiers are pseudonymous
/// personal data: never log, serialize to an exported diagnostic, or persist.
/// Levels must be independently re-derived/attested from the V9 comparator
/// before any operational calibration; this DTO does not certify extraction.
/// </summary>
public sealed record CandidateNominalV9LevelEvidence(
    Guid SourceId,
    Guid CandidateId,
    CompositeNominalLevelV9 Person,
    CompositeNominalLevelV9 Mother);

public sealed record CandidateWeightedJointClassV9(
    IndependentMatchLabel Label,
    int TrainingPairCount,
    int IndependentTrainingGroups,
    decimal TotalDesignWeight,
    decimal EffectiveSampleSize);

public sealed record CandidateWeightedJointCellV9(
    CompositeNominalLevelV9 Person,
    CompositeNominalLevelV9 Mother,
    int MatchedObservedPairs,
    int UnmatchedObservedPairs,
    decimal MatchedObservedWeight,
    decimal UnmatchedObservedWeight,
    decimal M,
    decimal U);

/// <summary>
/// Aggregate-only TRAIN diagnosis. Does NOT contain identifiers, names,
/// per-pair weights/evidence, classification labels, or an executable model.
/// </summary>
public sealed record CandidateWeightedJointDiagnosticV9(
    string Version,
    string Status,
    string NominalContractVersion,
    string SamplingDesignVersion,
    string FrameFingerprint,
    string SelectionFingerprint,
    string LabelingReference,
    string PartitionReference,
    int EvaluationPairCount,
    decimal SmoothingAlpha,
    CandidateWeightedJointClassV9 Matched,
    CandidateWeightedJointClassV9 Unmatched,
    int BothClassesObservedCells,
    IReadOnlyList<CandidateWeightedJointCellV9> Cells,
    string EvidenceGeneration = "CALLER_LEVEL_SIDECAR_UNATTESTED",
    string SmoothingBasis = "LEGACY_RAW_DESIGN_WEIGHT_SCALE");

/// <summary>
/// Design-weighted 9x9 joint person/mother *diagnostic* using the existing
/// independently-labeled, two-stage SRS candidate corpus. The birth blocking
/// of CandidateSamplingDesign is NOT the current runtime's dynamic, SQL
/// materialized blocking; this does not certify operational V9 m/u.
/// </summary>
public static class CandidateWeightedJointNominalEstimatorV9
{
    public const string Version = "CANDIDATE_WEIGHTED_NOMINAL_JOINT_V9_DIAGNOSTIC_2";
    public const string SmoothingBasis = "KISH_EFFECTIVE_SAMPLE_SIZE";
    public const string Status = "DIAGNOSTIC_ONLY_BIRTH_BLOCKING_NOT_RUNTIME_SQL_CERTIFIED";

    private static readonly CompositeNominalLevelV9[] Levels =
        Enum.GetValues<CompositeNominalLevelV9>();

    /// <summary>
    /// Explicit descriptive pseudocount convention for weighted diagnostics:
    /// the observed empirical probability is unchanged, but smoothing alpha
    /// acts in units of Kish effective observations, not design-weight totals.
    /// Therefore multiplying every design weight by a constant cannot change
    /// the smoothed probability. This is NOT design-based uncertainty,
    /// empirical Bayes calibration or operational authority.
    /// </summary>
    public static decimal EssNormalizedSmoothedProbability(
        decimal observedCellWeight,
        decimal totalClassWeight,
        decimal kishEffectiveSampleSize,
        decimal smoothingAlpha,
        int exclusiveCellCount)
    {
        if (observedCellWeight < 0m || totalClassWeight <= 0m ||
            observedCellWeight > totalClassWeight || kishEffectiveSampleSize <= 0m ||
            smoothingAlpha <= 0m || smoothingAlpha > 1000m || exclusiveCellCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(observedCellWeight),
                "Peso, ESS, suavização ou cardinalidade de estados inválidos.");

        var normalizedObserved = observedCellWeight / totalClassWeight * kishEffectiveSampleSize;
        return (normalizedObserved + smoothingAlpha) /
            (kishEffectiveSampleSize + smoothingAlpha * exclusiveCellCount);
    }

    /// <summary>
    /// Preferred V9 diagnostic path: calculate both nominal levels from the
    /// complete, restricted raw evidence inventory. Never trust a caller's
    /// claimed V9 enum values when raw comparisons are available. This proves
    /// deterministic recomputation, NOT the authenticity of the raw source.
    /// </summary>
    public static CandidateWeightedJointDiagnosticV9 EstimateFromRawEvidence(
        ValidatedCandidateCorpus corpus,
        IReadOnlyList<CandidateNominalV9RawEvidence> rawEvidence,
        decimal smoothingAlpha,
        decimal minimumEffectiveSampleSize = 2m)
    {
        var derived = CandidateNominalV9EvidenceExtractor.Extract(corpus, rawEvidence);
        return Estimate(corpus, derived, smoothingAlpha, minimumEffectiveSampleSize)
            with { EvidenceGeneration = CandidateNominalV9EvidenceExtractor.Version };
    }

    public static CandidateWeightedJointDiagnosticV9 Estimate(
        ValidatedCandidateCorpus corpus,
        IReadOnlyList<CandidateNominalV9LevelEvidence> levelEvidence,
        decimal smoothingAlpha,
        decimal minimumEffectiveSampleSize = 2m)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(levelEvidence);
        if (smoothingAlpha <= 0m || smoothingAlpha > 1000m ||
            minimumEffectiveSampleSize < 1m)
            throw new ArgumentOutOfRangeException(nameof(smoothingAlpha));

        var capture = corpus.Capture;
        var manifest = corpus.Manifest;
        var rows = corpus.Observations;
        if (capture is null || manifest is null || rows is null ||
            !manifest.Complete || string.IsNullOrWhiteSpace(manifest.Reference) ||
            string.IsNullOrWhiteSpace(manifest.PartitionReference) ||
            string.IsNullOrWhiteSpace(capture.FrameFingerprint) ||
            string.IsNullOrWhiteSpace(capture.SelectionFingerprint) ||
            manifest.FrameFingerprint != capture.FrameFingerprint ||
            manifest.SelectionFingerprint != capture.SelectionFingerprint ||
            manifest.DesignVersion != CandidateSamplingDesign.Version ||
            capture.DesignVersion != CandidateSamplingDesign.Version ||
            manifest.BlockingVersion != BirthBlockingPlan.Version ||
            capture.BlockingVersion != BirthBlockingPlan.Version ||
            manifest.FeatureVersion != CandidateLabeling.FeatureVersion)
            throw new InvalidOperationException(
                "V9 ponderada requer corpus validado com frame, desenho, partição e rótulos congelados.");

        // Complete inventory, including EVALUATION, prevents an untracked
        // sidecar from hiding hard cases or adding pair evidence after labels.
        var features = new Dictionary<(Guid Source, Guid Candidate), CandidateNominalV9LevelEvidence>();
        foreach (var row in levelEvidence)
        {
            if (row is null || row.SourceId == Guid.Empty || row.CandidateId == Guid.Empty ||
                !Enum.IsDefined(row.Person) || !Enum.IsDefined(row.Mother) ||
                !features.TryAdd((row.SourceId, row.CandidateId), row))
                throw new InvalidOperationException("Sidecar nominal V9 contém níveis, chaves ou duplicatas inválidos.");
        }
        if (features.Count != rows.Count)
            throw new InvalidOperationException("Sidecar V9 incompleto ou excedente.");

        var seen = new HashSet<(Guid Source, Guid Candidate)>();
        var train = new List<(ValidatedCandidateObservation Observation, CandidateNominalV9LevelEvidence Levels)>();
        var evaluation = new List<ValidatedCandidateObservation>();
        var trainGroups = new HashSet<Guid>();
        var evaluationGroups = new HashSet<Guid>();

        foreach (var row in rows)
        {
            if (row is null || row.Sample is null || row.IndependenceGroupId == Guid.Empty ||
                row.Label == IndependentMatchLabel.Inconclusive ||
                !Enum.IsDefined(row.Label) || !Enum.IsDefined(row.Partition) ||
                !seen.Add((row.Sample.SourceId, row.Sample.CandidateId)) ||
                !features.TryGetValue((row.Sample.SourceId, row.Sample.CandidateId), out var states))
                throw new InvalidOperationException("Inventário V9 não coincide com a captura rotulada.");

            var (probability, weight) = CandidateSamplingDesign.Inclusion(
                capture.FrameSize, capture.SelectedSources,
                row.Sample.StratumPopulation, row.Sample.StratumSampleSize);
            if (probability != row.Sample.InclusionProbability ||
                weight != row.Sample.DesignWeight)
                throw new InvalidOperationException("Peso/probabilidade V9 diverge do desenho amostral.");

            if (row.Partition == CandidateCorpusPartition.Training)
            {
                train.Add((row, states));
                trainGroups.Add(row.IndependenceGroupId);
            }
            else
            {
                evaluation.Add(row);
                evaluationGroups.Add(row.IndependenceGroupId);
            }
        }

        if (trainGroups.Overlaps(evaluationGroups) || evaluation.Count == 0 ||
            train.Count == 0 || seen.Count != features.Count ||
            !evaluation.Any(x => x.Label == IndependentMatchLabel.Match) ||
            !evaluation.Any(x => x.Label == IndependentMatchLabel.NonMatch))
            throw new InvalidOperationException(
                "TRAIN e EVALUATION devem ser disjuntos por grupo e cobrir ambas as classes.");

        var (matched, supportM) = WeightedClass(IndependentMatchLabel.Match);
        var (unmatched, supportU) = WeightedClass(IndependentMatchLabel.NonMatch);

        // Pseudocounts belong to the effective-observation scale, not the
        // arbitrary inverse-inclusion weight scale. Retain raw observed
        // design weights unmodified in the diagnostic support columns.
        var cells = new List<CandidateWeightedJointCellV9>(Levels.Length * Levels.Length);
        foreach (var person in Levels)
        foreach (var mother in Levels)
        {
            var key = (person, mother);
            var m = supportM.GetValueOrDefault(key);
            var u = supportU.GetValueOrDefault(key);
            cells.Add(new CandidateWeightedJointCellV9(
                person, mother, m.Count, u.Count,
                m.Weight, u.Weight,
                EssNormalizedSmoothedProbability(
                    m.Weight, matched.TotalDesignWeight,
                    matched.EffectiveSampleSize, smoothingAlpha, Levels.Length * Levels.Length),
                EssNormalizedSmoothedProbability(
                    u.Weight, unmatched.TotalDesignWeight,
                    unmatched.EffectiveSampleSize, smoothingAlpha, Levels.Length * Levels.Length)));
        }

        return new CandidateWeightedJointDiagnosticV9(
            Version, Status, CompositeNominalComparisonV9.ContractVersion,
            CandidateSamplingDesign.Version, capture.FrameFingerprint,
            capture.SelectionFingerprint, manifest.Reference, manifest.PartitionReference,
            evaluation.Count, smoothingAlpha, matched, unmatched,
            cells.Count(c => c.MatchedObservedPairs > 0 && c.UnmatchedObservedPairs > 0),
            Array.AsReadOnly(cells.ToArray()), SmoothingBasis: SmoothingBasis);

        (CandidateWeightedJointClassV9 Summary,
         Dictionary<(CompositeNominalLevelV9, CompositeNominalLevelV9), (int Count, decimal Weight)> Support)
            WeightedClass(IndependentMatchLabel label)
        {
            var selected = train.Where(x => x.Observation.Label == label).ToArray();
            var groups = selected.Select(x => x.Observation.IndependenceGroupId).Distinct().Count();
            if (selected.Length == 0 || groups < 2)
                throw new InvalidOperationException("V9 requer pelo menos dois grupos TRAIN independentes por classe.");

            var total = 0m;
            var squares = 0m;
            var support = new Dictionary<(CompositeNominalLevelV9, CompositeNominalLevelV9), (int Count, decimal Weight)>();
            foreach (var (observation, levels) in selected)
            {
                var weight = observation.Sample.DesignWeight;
                total += weight;
                squares += weight * weight;
                var key = (levels.Person, levels.Mother);
                support.TryGetValue(key, out var previous);
                support[key] = (checked(previous.Count + 1), previous.Weight + weight);
            }
            var effective = total * total / squares;
            if (effective < minimumEffectiveSampleSize)
                throw new InvalidOperationException("V9 diagnóstico: tamanho efetivo insuficiente por classe.");
            return (new CandidateWeightedJointClassV9(
                label, selected.Length, groups, total, effective), support);
        }
    }
}
