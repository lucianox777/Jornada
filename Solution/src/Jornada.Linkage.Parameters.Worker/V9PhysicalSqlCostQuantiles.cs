namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Descriptive, nearest-rank tail latency for physical SQL runs. No synthetic
/// certification or implicit runtime gate; callers must supply measured timings.
/// </summary>
public static class V9PhysicalSqlCostQuantiles
{
    public sealed record Summary(int SampleCount, double P95Milliseconds, double P99Milliseconds,
        double MaximumMilliseconds);

    public static Summary FromMeasuredMilliseconds(IEnumerable<double> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var values = samples.ToArray();
        if (values.Length == 0 || values.Any(x => !double.IsFinite(x) || x < 0))
            throw new InvalidDataException("Amostras SQL reais, finitas e não negativas são obrigatórias.");
        Array.Sort(values);
        static double Rank(double[] sorted, int numerator) =>
            sorted[(int)((((long)sorted.Length * numerator) + 99) / 100) - 1];
        return new Summary(values.Length, Rank(values, 95), Rank(values, 99), values[^1]);
    }
}
