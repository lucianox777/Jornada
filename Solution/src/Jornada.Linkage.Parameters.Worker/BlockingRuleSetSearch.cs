using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record BlockingRuleSetSearchOptions(
    int MaxFieldsPerPass = 2,
    int MaxPasses = 2,
    int PrimitivePoolSize = 8,
    double MinimumTrueMatchRecall = 0.95d,
    bool RequireObservedNonMatchSupport = false,
    int CandidateRuleSetCap = 2048,
    bool RequireExhaustivePrimitivePool = false)
{
    public void Validate()
    {
        if (MaxFieldsPerPass is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(MaxFieldsPerPass));
        if (MaxPasses is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(MaxPasses));
        if (PrimitivePoolSize is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(PrimitivePoolSize));
        if (CandidateRuleSetCap is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(CandidateRuleSetCap));
        if (MinimumTrueMatchRecall is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(MinimumTrueMatchRecall));
    }
}

public sealed record BlockingRuleSetCandidateEnumeration(
    IReadOnlyList<IReadOnlyList<LinkageBlockingPass>> Candidates,
    int EvaluatedPrimitiveCount,
    int RetainedPrimitiveCount,
    long EnumeratedUnionCount,
    bool ExhaustivePrimitivePool);

/// <summary>
/// Busca bounded e determinística. Primeiro avalia todos os passes primitivos com até N campos.
/// O pool limitado preserva simultaneamente alternativas fortes em recall e em redução, evitando
/// que variantes quase universais ocupem toda a busca. Quando habilitado, testa também pares dos
/// passes retidos. O modo opt-in de 3/4 passes enumera uniões deduplicadas com orçamento
/// estrito de candidatos (sem truncamento silencioso). A promoção é fail-closed: recall mínimo é
/// restrição; entre alternativas que a
/// satisfazem, a redução do universo é o objetivo primário. Quando a calibração exige suporte u
/// observável, regras que retenham zero não-vínculos são descartadas antes da escolha final.
/// </summary>
public static class BlockingRuleSetSearch
{
    public const string MethodVersion = "BLOCKING_RULESET_SEARCH_V4";
    public const int MaxCandidateRuleSets = 2048;

    public static BlockingRuleSetOptimizationResult SearchBest(
        IReadOnlyCollection<BlockingFeatureObservation> observations,
        IReadOnlyCollection<string> features,
        BlockingRuleSetSearchOptions? options = null)
    {
        options ??= new BlockingRuleSetSearchOptions();
        var enumeration = EnumerateCandidateRuleSets(observations, features, options);
        return BlockingRuleSetOptimizer.SelectBest(
            observations, enumeration.Candidates,
            options.MinimumTrueMatchRecall, options.RequireObservedNonMatchSupport);
    }

    /// <summary>Returns EVERY union from the declared primitive pool, before final selection.</summary>
    public static BlockingRuleSetCandidateEnumeration EnumerateCandidateRuleSets(
        IReadOnlyCollection<BlockingFeatureObservation> observations,
        IReadOnlyCollection<string> features,
        BlockingRuleSetSearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(features);
        options ??= new BlockingRuleSetSearchOptions();
        options.Validate();

        var canonicalFeatures = features
            .Where(static feature => !string.IsNullOrWhiteSpace(feature))
            .Select(static feature => feature.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static feature => feature, StringComparer.Ordinal)
            .ToArray();
        if (canonicalFeatures.Length == 0)
            throw new ArgumentException("O espaço de busca não possui atributos.", nameof(features));

        var primitiveResults = GeneratePrimitivePasses(canonicalFeatures, options.MaxFieldsPerPass)
            .Select(pass => BlockingRuleSetOptimizer.SelectBest(
                observations,
                new IReadOnlyList<LinkageBlockingPass>[] { new[] { pass } },
                minimumTrueMatchRecall: 0d))
            .ToArray();
        if (options.RequireExhaustivePrimitivePool && primitiveResults.Length > options.PrimitivePoolSize)
            throw new InvalidOperationException(
                $"Exhaustive primitive search requires {primitiveResults.Length} primitives; pool={options.PrimitivePoolSize}.");
        var ranked = BuildPrimitivePool(primitiveResults, options)
            .Select(static result => result.Passes[0])
            .ToArray();

        // Never clip combinations to make a workload appear successful.
        // The complete set of unions up to MaxPasses is evaluated or the
        // calibration explicitly fails. The cap bounds CPU/memory when an
        // operator requests many primitives with four complementary passes.
        long candidateCount = 0;
        long combinations = 1;
        for (var width = 1; width <= Math.Min(options.MaxPasses, ranked.Length); width++)
        {
            combinations = checked(combinations * (ranked.Length - width + 1) / width);
            candidateCount = checked(candidateCount + combinations);
            if (candidateCount > options.CandidateRuleSetCap)
                throw new InvalidOperationException(
                    $"Blocking search requires {candidateCount}+ candidate unions; " +
                    $"cap={options.CandidateRuleSetCap}. Reduce PrimitivePoolSize/MaxPasses " +
                    "explicitly instead of silently truncating the search.");
        }

        var candidates = new List<IReadOnlyList<LinkageBlockingPass>>((int)candidateCount);
        for (var width = 1; width <= Math.Min(options.MaxPasses, ranked.Length); width++)
        foreach (var selected in Combinations(ranked, width))
            candidates.Add(selected);

        if (candidates.Count != candidateCount)
            throw new InvalidOperationException("Blocking search incomplete candidate enumeration.");

        return new BlockingRuleSetCandidateEnumeration(
            candidates, primitiveResults.Length, ranked.Length, candidateCount,
            primitiveResults.Length == ranked.Length);
    }

    private static IReadOnlyList<BlockingRuleSetOptimizationResult> BuildPrimitivePool(
        IReadOnlyCollection<BlockingRuleSetOptimizationResult> evaluated,
        BlockingRuleSetSearchOptions options)
    {
        var selected = new List<BlockingRuleSetOptimizationResult>(options.PrimitivePoolSize);
        var signatures = new HashSet<string>(StringComparer.Ordinal);

        void Add(BlockingRuleSetOptimizationResult candidate)
        {
            if (selected.Count < options.PrimitivePoolSize && signatures.Add(candidate.CanonicalSignature))
                selected.Add(candidate);
        }

        // Se um passe isolado já satisfaz a restrição, reserva a melhor solução de um passe
        // segundo o objetivo vigente. Isso também torna PrimitivePoolSize=1 semanticamente útil.
        var bestEligibleSingle = evaluated
            .Where(result => result.Diagnostic.TrueMatchRecall >= options.MinimumTrueMatchRecall)
            .Where(result => !options.RequireObservedNonMatchSupport || result.Diagnostic.NonMatchRetention > 0d)
            .OrderByDescending(static result => result.Diagnostic.ReductionRatio)
            .ThenByDescending(static result => result.Diagnostic.TrueMatchRecall)
            .ThenByDescending(static result => result.Diagnostic.CompleteMatchCoverage)
            .ThenBy(static result => result.FieldClauseCount)
            .ThenBy(static result => result.CanonicalSignature, StringComparer.Ordinal)
            .FirstOrDefault();
        if (bestEligibleSingle is not null)
            Add(bestEligibleSingle);

        var byRecall = evaluated
            .OrderByDescending(static result => result.Diagnostic.TrueMatchRecall)
            .ThenByDescending(static result => result.Diagnostic.ReductionRatio)
            .ThenByDescending(static result => result.Diagnostic.CompleteMatchCoverage)
            .ThenBy(static result => result.FieldClauseCount)
            .ThenBy(static result => result.CanonicalSignature, StringComparer.Ordinal)
            .ToArray();
        var byReduction = evaluated
            .OrderByDescending(static result => result.Diagnostic.ReductionRatio)
            .ThenByDescending(static result => result.Diagnostic.TrueMatchRecall)
            .ThenByDescending(static result => result.Diagnostic.CompleteMatchCoverage)
            .ThenBy(static result => result.FieldClauseCount)
            .ThenBy(static result => result.CanonicalSignature, StringComparer.Ordinal)
            .ToArray();

        var recallIndex = 0;
        var reductionIndex = 0;
        while (selected.Count < options.PrimitivePoolSize &&
               (recallIndex < byRecall.Length || reductionIndex < byReduction.Length))
        {
            if (recallIndex < byRecall.Length)
                Add(byRecall[recallIndex++]);
            if (selected.Count >= options.PrimitivePoolSize)
                break;
            if (reductionIndex < byReduction.Length)
                Add(byReduction[reductionIndex++]);
        }

        return selected;
    }

    private static IEnumerable<LinkageBlockingPass> GeneratePrimitivePasses(
        IReadOnlyList<string> features,
        int maxFieldsPerPass)
    {
        var ordinal = 0;
        for (var size = 1; size <= Math.Min(maxFieldsPerPass, features.Count); size++)
        foreach (var fields in Combinations(features, size))
        {
            ordinal++;
            yield return LinkageBlockingPass.Create($"P{ordinal:D3}", fields);
        }
    }

    private static IEnumerable<IReadOnlyList<T>> Combinations<T>(IReadOnlyList<T> values, int size)
    {
        var buffer = new T[size];
        return Recurse(0, 0);

        IEnumerable<IReadOnlyList<T>> Recurse(int start, int depth)
        {
            if (depth == size)
            {
                yield return buffer.ToArray();
                yield break;
            }

            for (var index = start; index <= values.Count - (size - depth); index++)
            {
                buffer[depth] = values[index];
                foreach (var item in Recurse(index + 1, depth + 1))
                    yield return item;
            }
        }
    }
}
