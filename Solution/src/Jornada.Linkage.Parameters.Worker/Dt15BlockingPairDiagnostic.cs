using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Early DT-15 evidence: evaluate existing ACTIVE passes and suggested DRAFT passes
/// on the EXACT SAME in-memory, labeled candidate-training pairs. This is ONLY a
/// blocking diagnostic: neither FS decision replay nor representative validation.
/// </summary>
public static class Dt15BlockingPairDiagnostic
{
    public const string MethodVersion = "DT15_BLOCKING_TRAINING_PAIR_V1";
    public const string Comparable = "COMPARAVEL_APENAS_BLOCKING_TREINO";
    public const string NoActiveModel = "SEM_MODELO_ATIVO_BASE";
    public const string NoActiveRuleSet = "SEM_RULESET_ATIVO";
    public const string IncompatibleVersion = "NAO_COMPARAVEL_VERSAO";
    public const string IncompatibleProjection = "NAO_COMPARAVEL_PROJECAO";
    public const string IncompatibleFeatures = "NAO_COMPARAVEL_ATRIBUTOS";

    public static Dt15BlockingPairDiagnosticResult Compare(
        IReadOnlyCollection<BlockingFeatureObservation> sameTrainingObservations,
        IReadOnlyList<LinkageBlockingPass> draftPasses,
        Dt15ActiveBlockingSnapshot? active,
        string draftAlgorithmVersion,
        string draftNormalizationVersion)
    {
        ArgumentNullException.ThrowIfNull(sameTrainingObservations);
        ArgumentNullException.ThrowIfNull(draftPasses);
        if (draftPasses.Count == 0)
            throw new ArgumentException("RASCUNHO não pode ficar sem passes.", nameof(draftPasses));
        if (sameTrainingObservations.Count == 0)
            throw new ArgumentException("Não existe amostra pareada para a comparação.", nameof(sameTrainingObservations));

        var matchedWeight = sameTrainingObservations.Where(static x => x.IsReferenceMatch).Sum(static x => x.Weight);
        var nonmatchedWeight = sameTrainingObservations.Where(static x => !x.IsReferenceMatch).Sum(static x => x.Weight);
        var suggested = BlockingRuleSetDiagnostic.Analyze(sameTrainingObservations, draftPasses);

        Dt15BlockingPairDiagnosticResult Result(string status, BlockingRuleSetDiagnosticResult? current = null) =>
            new(MethodVersion, status, active?.ModelId, active?.Version,
                active?.RuleSetFingerprintSha256,
                matchedWeight, nonmatchedWeight, current, suggested);

        if (active is null)
            return Result(NoActiveModel);
        if (active.Passes is null || active.Passes.Count == 0)
            return Result(NoActiveRuleSet);
        if (!string.Equals(active.AlgorithmVersion, draftAlgorithmVersion, StringComparison.Ordinal)
            || !string.Equals(active.NormalizationVersion, draftNormalizationVersion, StringComparison.Ordinal))
            return Result(IncompatibleVersion);
        if (!string.Equals(active.ProjectionSchemaVersion, PersonResolutionProjectionContract.SchemaVersion,
                StringComparison.Ordinal)
            || !string.Equals(active.ProjectionFingerprintSha256,
                PersonResolutionProjectionContract.FingerprintSha256, StringComparison.OrdinalIgnoreCase))
            return Result(IncompatibleProjection);

        var supported = BlockingCandidateFeatureCatalog.CalibratorCandidates.ToHashSet(StringComparer.Ordinal);
        if (active.Passes.SelectMany(static p => p.Fields).Any(field => !supported.Contains(field)))
            return Result(IncompatibleFeatures);

        var current = BlockingRuleSetDiagnostic.Analyze(sameTrainingObservations, active.Passes);
        return Result(Comparable, current);
    }
}

public sealed record Dt15ActiveBlockingSnapshot(
    Guid ModelId,
    int Version,
    string AlgorithmVersion,
    string NormalizationVersion,
    string? RuleSetFingerprintSha256,
    string? ProjectionSchemaVersion,
    string? ProjectionFingerprintSha256,
    IReadOnlyList<LinkageBlockingPass>? Passes);

public sealed record Dt15BlockingPairDiagnosticResult(
    string MethodVersion,
    string Status,
    Guid? ActiveModelId,
    int? ActiveModelVersion,
    string? ActiveRuleSetFingerprintSha256,
    decimal MatchedPairWeight,
    decimal NonMatchedPairWeight,
    BlockingRuleSetDiagnosticResult? Active,
    BlockingRuleSetDiagnosticResult Draft)
{
    public bool IsComparable => Status == Dt15BlockingPairDiagnostic.Comparable;
    public double? RecallDelta => Active is null ? null : Draft.TrueMatchRecall - Active.TrueMatchRecall;
    public double? ReductionDelta => Active is null ? null : Draft.ReductionRatio - Active.ReductionRatio;
}
