using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record BlockingRuleSetSearchOptions(
    int MaxFieldsPerPass = 2,
    int MaxPasses = 2,
    int PrimitivePoolSize = 8,
    double MinimumTrueMatchRecall = 0.95d)
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
/// Busca bounded e determinística. Primeiro avalia todos os passes primitivos com até N campos,
/// retém um pool explicitamente limitado e, quando habilitado, testa também pares desses passes.
/// O limite é parte do algoritmo e evita explosão combinatória sobre corpora grandes.
/// A promoção é fail-closed: se nenhuma alternativa atingir o recall mínimo, nenhum ruleset é publicado.
/// </summary>
public static class BlockingRuleSetSearch
{
    public const string MethodVersion = "BLOCKING_RULESET_SEARCH_V1";

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

        var primitives = GeneratePrimitivePasses(canonicalFeatures, options.MaxFieldsPerPass).ToArray();
        var ranked = primitives
            .Select(pass => BlockingRuleSetOptimizer.SelectBest(
                observations,
                new IReadOnlyList<LinkageBlockingPass>[] { new[] { pass } },
                minimumTrueMatchRecall: 0d))
            .OrderByDescending(static result => result.Diagnostic.TrueMatchRecall)
            .ThenByDescending(static result => result.Diagnostic.ReductionRatio)
            .ThenByDescending(static result => result.Diagnostic.CompleteMatchCoverage)
            .ThenBy(static result => result.FieldClauseCount)
            .ThenBy(static result => result.CanonicalSignature, StringComparer.Ordinal)
            .Take(options.PrimitivePoolSize)
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
            options.MinimumTrueMatchRecall);
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
