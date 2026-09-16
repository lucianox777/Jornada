using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record BlockingRuleSetSearchOptions(
    int MaxFieldsPerPass = 2,
    int MaxPasses = 2,
    int PrimitivePoolSize = 8,
    double MinimumTrueMatchRecall = 0.95d,
    bool RequireObservedNonMatchSupport = false)
{
    public void Validate()
    {
        if (MaxFieldsPerPass is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(MaxFieldsPerPass));
        if (MaxPasses is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(MaxPasses));
        if (PrimitivePoolSize is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(PrimitivePoolSize));
        if (MinimumTrueMatchRecall is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(MinimumTrueMatchRecall));
    }
}

/// <summary>
/// Busca bounded e determinística. Primeiro avalia todos os passes primitivos com até N campos.
/// O pool limitado preserva simultaneamente alternativas fortes em recall e em redução, evitando
/// que variantes quase universais ocupem toda a busca. Quando habilitado, testa também pares dos
/// passes retidos. A promoção é fail-closed: recall mínimo é restrição; entre alternativas que a
/// satisfazem, a redução do universo é o objetivo primário. Quando a calibração exige suporte u
/// observável, regras que retenham zero não-vínculos são descartadas antes da escolha final.
/// </summary>
public static class BlockingRuleSetSearch
{
    public const string MethodVersion = "BLOCKING_RULESET_SEARCH_V2";

    public static BlockingRuleSetOptimizationResult SearchBest(
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
        var ranked = BuildPrimitivePool(primitiveResults, options)
            .Select(static result => result.Passes[0])
            .ToArray();

        var candidates = new List<IReadOnlyList<LinkageBlockingPass>>(ranked.Length * ranked.Length);
        foreach (var primitive in ranked)
            candidates.Add(new[] { primitive });

        if (options.MaxPasses >= 2)
        {
            for (var left = 0; left < ranked.Length; left++)
            for (var right = left + 1; right < ranked.Length; right++)
                candidates.Add(new[] { ranked[left], ranked[right] });
        }

        return BlockingRuleSetOptimizer.SelectBest(
            observations,
            candidates,
            options.MinimumTrueMatchRecall,
            options.RequireObservedNonMatchSupport);
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
        // segundo o objetivo V2. Isso também torna PrimitivePoolSize=1 semanticamente útil.
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

    private static IEnumerable<IReadOnlyList<string>> Combinations(IReadOnlyList<string> values, int size)
    {
        var buffer = new string[size];
        return Recurse(0, 0);

        IEnumerable<IReadOnlyList<string>> Recurse(int start, int depth)
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
