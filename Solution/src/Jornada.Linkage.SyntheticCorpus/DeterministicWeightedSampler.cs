namespace Jornada.Linkage.SyntheticCorpus;

/// <summary>
/// Amostragem ponderada determinística sobre frequências inteiras.
/// A ordem física de entrada não participa do resultado: CanonicalKey define a ordem estável.
/// </summary>
public sealed record WeightedValue<T>(string CanonicalKey, T Value, ulong Weight);

public sealed class DeterministicWeightedSampler<T>
{
    public const string MethodVersion = "INTEGER_CUMULATIVE_WEIGHTED_SAMPLER_V1";

    private readonly Entry[] entries;

    private sealed record Entry(T Value, ulong CumulativeWeight);

    public DeterministicWeightedSampler(IEnumerable<WeightedValue<T>> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var ordered = values
            .OrderBy(x => x.CanonicalKey, StringComparer.Ordinal)
            .ToArray();

        if (ordered.Length == 0)
            throw new ArgumentException("Amostrador ponderado exige ao menos um valor.", nameof(values));
        if (ordered.Any(x => string.IsNullOrWhiteSpace(x.CanonicalKey)))
            throw new ArgumentException("CanonicalKey não pode ser vazia.", nameof(values));
        if (ordered.Any(x => x.Weight == 0))
            throw new ArgumentException("Pesos devem ser inteiros positivos.", nameof(values));

        var duplicate = ordered
            .GroupBy(x => x.CanonicalKey, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"CanonicalKey duplicada: {duplicate.Key}.", nameof(values));

        entries = new Entry[ordered.Length];
        ulong total = 0;
        for (var i = 0; i < ordered.Length; i++)
        {
            try
            {
                total = checked(total + ordered[i].Weight);
            }
            catch (OverflowException ex)
            {
                throw new ArgumentException("Soma de pesos excede UInt64.", nameof(values), ex);
            }

            entries[i] = new Entry(ordered[i].Value, total);
        }

        TotalWeight = total;
    }

    public ulong TotalWeight { get; }

    public T Next(Xoshiro256StarStar random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var target = random.NextUInt64(TotalWeight);

        var lo = 0;
        var hi = entries.Length - 1;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (target < entries[mid].CumulativeWeight)
                hi = mid;
            else
                lo = mid + 1;
        }

        return entries[lo].Value;
    }
}
