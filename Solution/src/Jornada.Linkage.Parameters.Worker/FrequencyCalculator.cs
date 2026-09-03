namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Utilitário reservado para profiling/term-frequency adjustment futuro.
/// O baseline FELLEGI_SUNTER_ANCHORED_V1 não materializa frequências de alta cardinalidade por modelo.
/// </summary>
public static class FrequencyCalculator
{
    public static IReadOnlyDictionary<string, (long Occurrences, decimal Frequency)> Calculate(IEnumerable<string?> values)
    {
        var normalized = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToArray();
        if (normalized.Length == 0)
            return new Dictionary<string, (long, decimal)>();

        return normalized
            .GroupBy(v => v, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => ((long)g.LongCount(), (decimal)g.LongCount() / normalized.LongLength),
                StringComparer.Ordinal);
    }
}
