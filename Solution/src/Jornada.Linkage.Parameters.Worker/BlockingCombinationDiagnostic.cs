namespace Jornada.Linkage.Parameters.Worker;

public sealed record BlockingCombinationDiagnosticResult(
    IReadOnlyList<string> Fields,
    double TrueMatchRecall,
    double NonMatchRetention,
    double ReductionRatio,
    double IncrementalRecallVsBestMember,
    double IncrementalReductionVsBestMember,
    decimal EffectiveObservedWeight);

/// <summary>
/// Read-only diagnostic for OR-combinations of candidate blocking fields/passes.
/// It measures union recall, non-match retention and incremental gain over the
/// best individual member. It never changes BirthBlockingPlan or operational policy.
/// </summary>
public static class BlockingCombinationDiagnostic
{
    public const string MethodVersion = "BLOCKING_COMBINATION_DIAGNOSTIC_V1";

    public static IReadOnlyList<BlockingCombinationDiagnosticResult> Analyze(
        IReadOnlyCollection<BlockingFeatureObservation> observations,
        int minSize = 2,
        int maxSize = 3)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0)
            throw new ArgumentException("O corpus de diagnóstico não pode ser vazio.", nameof(observations));
        if (minSize < 2 || maxSize < minSize)
            throw new ArgumentOutOfRangeException(nameof(minSize), "Intervalo de tamanho de combinação inválido.");

        foreach (var observation in observations)
        {
            ArgumentNullException.ThrowIfNull(observation);
            ArgumentNullException.ThrowIfNull(observation.Agreements);
            if (observation.Weight <= 0m)
                throw new ArgumentOutOfRangeException(nameof(observations), "Todos os pesos devem ser positivos.");
        }

        var fields = observations
            .SelectMany(static x => x.Agreements.Keys)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();

        if (fields.Length < minSize)
            return Array.Empty<BlockingCombinationDiagnosticResult>();

        var matchWeight = observations.Where(static x => x.IsReferenceMatch).Sum(static x => x.Weight);
        var nonMatchWeight = observations.Where(static x => !x.IsReferenceMatch).Sum(static x => x.Weight);
        if (matchWeight <= 0m || nonMatchWeight <= 0m)
            throw new ArgumentException("O corpus deve conter vínculos e não-vínculos de referência.", nameof(observations));

        var individual = BlockingFeatureDiagnostic.Analyze(observations).Fields
            .ToDictionary(static x => x.Field, StringComparer.Ordinal);

        var results = new List<BlockingCombinationDiagnosticResult>();
        for (var size = minSize; size <= Math.Min(maxSize, fields.Length); size++)
            foreach (var combination in Combinations(fields, size))
                results.Add(AnalyzeCombination(combination, observations, matchWeight, nonMatchWeight, individual));

        return results
            .OrderByDescending(static x => x.TrueMatchRecall)
            .ThenByDescending(static x => x.ReductionRatio)
            .ThenByDescending(static x => x.IncrementalRecallVsBestMember)
            .ThenBy(static x => string.Join("|", x.Fields), StringComparer.Ordinal)
            .ToArray();
    }

    private static BlockingCombinationDiagnosticResult AnalyzeCombination(
        IReadOnlyList<string> fields,
        IReadOnlyCollection<BlockingFeatureObservation> observations,
        decimal matchWeight,
        decimal nonMatchWeight,
        IReadOnlyDictionary<string, BlockingFieldDiagnostic> individual)
    {
        decimal observedWeight = 0m;
        decimal retainedMatch = 0m;
        decimal retainedNonMatch = 0m;

        foreach (var observation in observations)
        {
            var anyObserved = false;
            var retained = false;
            foreach (var field in fields)
            {
                if (!observation.Agreements.TryGetValue(field, out var value) || value is null) continue;
                anyObserved = true;
                retained |= value.Value;
            }

            if (!anyObserved) continue;
            observedWeight += observation.Weight;
            if (!retained) continue;
            if (observation.IsReferenceMatch) retainedMatch += observation.Weight;
            else retainedNonMatch += observation.Weight;
        }

        var recall = (double)(retainedMatch / matchWeight);
        var retention = (double)(retainedNonMatch / nonMatchWeight);
        var reduction = 1d - retention;
        var bestRecall = fields.Max(field => individual[field].TrueMatchRecall);
        var bestReduction = fields.Max(field => individual[field].ReductionRatio);

        return new BlockingCombinationDiagnosticResult(
            fields.ToArray(),
            recall,
            retention,
            reduction,
            recall - bestRecall,
            reduction - bestReduction,
            observedWeight);
    }

    private static IEnumerable<IReadOnlyList<string>> Combinations(string[] fields, int size)
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

            for (var i = start; i <= fields.Length - (size - depth); i++)
            {
                buffer[depth] = fields[i];
                foreach (var item in Recurse(i + 1, depth + 1))
                    yield return item;
            }
        }
    }
}
