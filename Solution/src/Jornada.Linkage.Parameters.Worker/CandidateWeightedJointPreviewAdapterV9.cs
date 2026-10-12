using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Non-operational bridge from independently labeled TRAIN diagnostics to
/// the existing C# Fellegi-Sunter V9 mathematical preview. It never issues
/// certification, enables AlgorithmVersion=V9, or alters a persisted model.
/// The sampled BirthBlockingPlan is NOT the operational temporal SQL frame.
/// </summary>
public static class CandidateWeightedJointPreviewAdapterV9
{
    public const string Version = "V9_JOINT_DIAGNOSTIC_TO_FS_PREVIEW_ONLY_1";

    /// <summary>
    /// Copy all 81 joint M/U cells after validating the aggregate diagnostic
    /// contract. Only levels re-extracted from raw evidence are accepted,
    /// never caller-provided level sidecars. This is still NOT source
    /// authentication, calibration approval or DT-15 authorization.
    /// </summary>
    public static IReadOnlyList<FellegiSunterJointNominalCellV9> FromTrainingDiagnostic(
        CandidateWeightedJointDiagnosticV9 diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (diagnostic.Version != CandidateWeightedJointNominalEstimatorV9.Version
            || diagnostic.Status != CandidateWeightedJointNominalEstimatorV9.Status
            || diagnostic.SmoothingBasis != CandidateWeightedJointNominalEstimatorV9.SmoothingBasis
            || diagnostic.NominalContractVersion != CompositeNominalComparisonV9.ContractVersion
            || diagnostic.SamplingDesignVersion != CandidateSamplingDesign.Version
            || diagnostic.EvidenceGeneration != CandidateNominalV9EvidenceExtractor.Version)
            throw new InvalidOperationException("Snapshot V9 não é uma auditoria TRAIN recalculada com contrato compatível.");

        if (string.IsNullOrWhiteSpace(diagnostic.FrameFingerprint)
            || string.IsNullOrWhiteSpace(diagnostic.SelectionFingerprint)
            || string.IsNullOrWhiteSpace(diagnostic.LabelingReference)
            || string.IsNullOrWhiteSpace(diagnostic.PartitionReference)
            || diagnostic.EvaluationPairCount <= 0
            || diagnostic.SmoothingAlpha <= 0m
            || diagnostic.SmoothingAlpha > 1000m)
            throw new InvalidOperationException("Snapshot V9 carece de proveniência e partição declaradas.");

        var matched = diagnostic.Matched;
        var unmatched = diagnostic.Unmatched;
        if (matched is null || unmatched is null
            || matched.Label != IndependentMatchLabel.Match
            || unmatched.Label != IndependentMatchLabel.NonMatch
            || matched.TrainingPairCount <= 0 || unmatched.TrainingPairCount <= 0
            || matched.IndependentTrainingGroups < 2
            || unmatched.IndependentTrainingGroups < 2
            || matched.EffectiveSampleSize < 2m
            || unmatched.EffectiveSampleSize < 2m
            || matched.TotalDesignWeight <= 0m
            || unmatched.TotalDesignWeight <= 0m)
            throw new InvalidOperationException("Snapshot V9 não possui suporte TRAIN independente suficiente.");

        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        if (diagnostic.Cells is null || diagnostic.Cells.Count != levels.Length * levels.Length)
            throw new InvalidOperationException("Snapshot V9 exige as 81 células de evidência.");

        var seen = new HashSet<(CompositeNominalLevelV9, CompositeNominalLevelV9)>();
        var copied = new List<FellegiSunterJointNominalCellV9>(diagnostic.Cells.Count);
        decimal sumM = 0m, sumU = 0m, weightM = 0m, weightU = 0m;
        long countM = 0L, countU = 0L;
        var observedBoth = 0;
        foreach (var cell in diagnostic.Cells)
        {
            if (cell is null || !Enum.IsDefined(cell.Person) || !Enum.IsDefined(cell.Mother)
                || !seen.Add((cell.Person, cell.Mother))
                || cell.M <= 0m || cell.M > 1m || cell.U <= 0m || cell.U > 1m
                || cell.MatchedObservedPairs < 0 || cell.UnmatchedObservedPairs < 0
                || cell.MatchedObservedWeight < 0m || cell.UnmatchedObservedWeight < 0m
                || (cell.MatchedObservedPairs == 0 && cell.MatchedObservedWeight != 0m)
                || (cell.UnmatchedObservedPairs == 0 && cell.UnmatchedObservedWeight != 0m)
                || (cell.MatchedObservedPairs > 0 && cell.MatchedObservedWeight <= 0m)
                || (cell.UnmatchedObservedPairs > 0 && cell.UnmatchedObservedWeight <= 0m))
                throw new InvalidOperationException("Snapshot V9 tem célula, probabilidade ou suporte inconsistente.");

            sumM += cell.M;
            sumU += cell.U;
            weightM += cell.MatchedObservedWeight;
            weightU += cell.UnmatchedObservedWeight;
            countM += cell.MatchedObservedPairs;
            countU += cell.UnmatchedObservedPairs;
            if (cell.MatchedObservedPairs > 0 && cell.UnmatchedObservedPairs > 0)
                observedBoth++;
            copied.Add(new FellegiSunterJointNominalCellV9(
                cell.Person, cell.Mother, cell.M, cell.U));
        }

        if (Math.Abs(sumM - 1m) > 0.000001m || Math.Abs(sumU - 1m) > 0.000001m
            || Math.Abs(weightM - matched.TotalDesignWeight) > 0.000001m
            || Math.Abs(weightU - unmatched.TotalDesignWeight) > 0.000001m
            || countM != matched.TrainingPairCount
            || countU != unmatched.TrainingPairCount
            || observedBoth != diagnostic.BothClassesObservedCells)
            throw new InvalidOperationException("Snapshot V9 diverge dos agregados TRAIN ou da normalização M/U.");

        return Array.AsReadOnly(copied.ToArray());
    }
}
