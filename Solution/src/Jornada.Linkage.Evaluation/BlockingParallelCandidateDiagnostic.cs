namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Read-only comparison of D, C and their exact deduplicated union on the SAME no-CPF,
/// inter-Gestor pair universe. Input pairs are opaque within the evaluator and NEVER
/// emitted in the report. This does not score, publish or activate a blocking policy.
/// </summary>
public static class BlockingParallelCandidateDiagnostic
{
    public const string MethodVersion = "BLOCKING_PARALLEL_SYNTHETIC_V1";
    public const string Universe = "NO_CPF_INTER_GESTOR_MATERIALIZED_PAIR_UNIVERSE";

    public static SyntheticParallelBlockingEvaluation Analyze(
        IReadOnlyCollection<ulong> dynamicCandidatePairs,
        IReadOnlyCollection<ulong> combinedCandidatePairs,
        IReadOnlyCollection<ulong> eligibleTruePairs,
        long eligiblePairCount,
        long combinedEligibleTruePairs,
        int maxCandidatePairs)
    {
        ArgumentNullException.ThrowIfNull(dynamicCandidatePairs);
        ArgumentNullException.ThrowIfNull(combinedCandidatePairs);
        ArgumentNullException.ThrowIfNull(eligibleTruePairs);
        ArgumentOutOfRangeException.ThrowIfNegative(eligiblePairCount);
        ArgumentOutOfRangeException.ThrowIfNegative(combinedEligibleTruePairs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCandidatePairs);

        var truth = eligibleTruePairs.ToHashSet();
        var dynamic = dynamicCandidatePairs.ToHashSet();
        var combined = combinedCandidatePairs.ToHashSet();
        if (truth.Count != eligibleTruePairs.Count
            || combinedEligibleTruePairs > truth.Count
            || eligiblePairCount < truth.Count)
        {
            throw new InvalidDataException(
                "Denominadores de verdade elegível inconsistentes no benchmark paralelo.");
        }

        var union = new HashSet<ulong>(dynamic);
        union.UnionWith(combined);
        if (union.Count > maxCandidatePairs)
            throw new InvalidOperationException(
                $"União D∪C exige {union.Count} pares, acima do limite seguro " +
                $"{maxCandidatePairs}; a avaliação falha sem truncar.");

        if (union.Count > eligiblePairCount)
            throw new InvalidDataException(
                "Pares candidatos fora do universo no-CPF/inter-Gestor do benchmark.");

        var shared = dynamic.Count + (long)combined.Count - union.Count;
        var dynamicOnly = dynamic.Count - shared;
        var combinedOnly = combined.Count - shared;
        var dynamicTrue = dynamic.LongCount(truth.Contains);
        var combinedTrue = combined.LongCount(truth.Contains);
        var unionTrue = union.LongCount(truth.Contains);
        var sharedTrue = dynamicTrue + combinedTrue - unionTrue;
        var dynamicOnlyTrue = dynamicTrue - sharedTrue;
        var combinedOnlyTrue = combinedTrue - sharedTrue;

        if (combinedTrue > combinedEligibleTruePairs)
            throw new InvalidDataException(
                "Combined recuperou mais verdades que pares com os dois lados elegíveis.");

        var possibleNonMatches = eligiblePairCount - truth.Count;
        return new SyntheticParallelBlockingEvaluation(
            MethodVersion, Universe,
            eligiblePairCount, truth.Count, combinedEligibleTruePairs,
            dynamic.Count, combined.Count, union.Count,
            shared, dynamicOnly, combinedOnly,
            dynamicTrue, combinedTrue, unionTrue,
            sharedTrue, dynamicOnlyTrue, combinedOnlyTrue,
            Rate(dynamicTrue, truth.Count),
            Rate(combinedTrue, truth.Count),
            Rate(combinedTrue, combinedEligibleTruePairs),
            Rate(unionTrue, truth.Count),
            Reduction(dynamic.Count, dynamicTrue, possibleNonMatches),
            Reduction(combined.Count, combinedTrue, possibleNonMatches),
            Reduction(union.Count, unionTrue, possibleNonMatches));
    }

    private static decimal Rate(long numerator, long denominator)
        => denominator == 0 ? 0m : (decimal)numerator / denominator;

    private static decimal Reduction(long candidates, long truePairs, long possibleNonMatches)
    {
        var retainedNonMatches = candidates - truePairs;
        if (retainedNonMatches < 0 || retainedNonMatches > possibleNonMatches)
            throw new InvalidDataException(
                "A quantidade de não-vínculos recuperados excede o universo comparável.");
        return possibleNonMatches == 0
            ? 0m
            : 1m - (decimal)retainedNonMatches / possibleNonMatches;
    }
}

public sealed record SyntheticParallelBlockingEvaluation(
    string MethodVersion,
    string Universe,
    long EligiblePairCount,
    long EligibleTruePairs,
    long CombinedEligibleTruePairs,
    long DynamicCandidatePairs,
    long CombinedCandidatePairs,
    long UnionCandidatePairs,
    long SharedCandidatePairs,
    long DynamicOnlyCandidatePairs,
    long CombinedOnlyCandidatePairs,
    long DynamicTruePairs,
    long CombinedTruePairs,
    long UnionTruePairs,
    long SharedTruePairs,
    long DynamicOnlyTruePairs,
    long CombinedOnlyTruePairs,
    decimal DynamicRecall,
    decimal CombinedRecall,
    decimal CombinedConditionalRecall,
    decimal UnionRecall,
    decimal DynamicReductionRatio,
    decimal CombinedReductionRatio,
    decimal UnionReductionRatio);
