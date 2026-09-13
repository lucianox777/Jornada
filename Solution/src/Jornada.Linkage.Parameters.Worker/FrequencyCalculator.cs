namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Calcula frequência empírica de valores para profiling e term-frequency adjustment.
/// Frequências populacionais externas (por exemplo IBGE) permanecem versionadas em
/// catálogo próprio; este utilitário não substitui a proveniência da referência externa.
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
