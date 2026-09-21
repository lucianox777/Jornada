namespace Jornada.Linkage.SyntheticCorpus;

public sealed record SyntheticFrequencyValue(string Value, long Frequency);

/// <summary>
/// Tradução do FreqSampler do gen_corpus_v2.py.
/// O oversampling da cauda altera somente a probabilidade de sorteio; o peso de
/// avaliação guarda o inverso do boost para reponderar ao universo nominal.
/// </summary>
public sealed class SyntheticFrequencySampler
{
    public const string MethodVersion = "PYTHON_V2_FREQ_SAMPLER_RULES_CSHARP_V1";

    private readonly string[] values;
    private readonly double[] cumulative;
    private readonly Dictionary<string, double> correction;

    public SyntheticFrequencySampler(
        IEnumerable<SyntheticFrequencyValue> source,
        double tailBoost,
        double tailQuantile = .25)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (double.IsNaN(tailBoost) || double.IsInfinity(tailBoost) || tailBoost <= 0)
            throw new ArgumentOutOfRangeException(nameof(tailBoost));
        if (double.IsNaN(tailQuantile) || tailQuantile <= 0 || tailQuantile >= 1)
            throw new ArgumentOutOfRangeException(nameof(tailQuantile));

        var pairs = source
            .Where(x => !string.IsNullOrWhiteSpace(x.Value) && x.Frequency > 0)
            .OrderByDescending(x => x.Frequency)
            .ThenBy(x => x.Value, StringComparer.Ordinal)
            .ToArray();
        if (pairs.Length == 0)
            throw new ArgumentException("Vocabulário vazio.", nameof(source));

        var cut = 0L;
        if (tailBoost != 1.0)
        {
            var rawIndex = (int)Math.Floor(pairs.Length * (1.0 - tailQuantile));
            var index = Math.Clamp(rawIndex, 0, pairs.Length - 1);
            cut = pairs[index].Frequency;
        }

        values = new string[pairs.Length];
        cumulative = new double[pairs.Length];
        correction = new Dictionary<string, double>(StringComparer.Ordinal);

        var acc = 0.0;
        for (var i = 0; i < pairs.Length; i++)
        {
            var boost = tailBoost != 1.0 && pairs[i].Frequency <= cut ? tailBoost : 1.0;
            acc += pairs[i].Frequency * boost;
            if (double.IsInfinity(acc) || double.IsNaN(acc))
                throw new ArgumentException("Peso acumulado inválido.", nameof(source));

            values[i] = pairs[i].Value;
            cumulative[i] = acc;
            correction[pairs[i].Value] = 1.0 / boost;
        }

        TotalWeight = acc;
        TailCutFrequency = cut;
    }

    public double TotalWeight { get; }
    public long TailCutFrequency { get; }

    public string Draw(Xoshiro256StarStar random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var target = random.NextUnitInterval() * TotalWeight;

        var lo = 0;
        var hi = cumulative.Length - 1;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            // Python usa bisect(cum, target): escolhe o primeiro cumulative > target.
            if (target < cumulative[mid])
                hi = mid;
            else
                lo = mid + 1;
        }

        return values[lo];
    }

    public double Weight(string value)
        => correction.TryGetValue(value, out var weight) ? weight : 1.0;
}
