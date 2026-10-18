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
    /// <summary>
    /// Summarizes repeated independent executions of the same physical SQL
    /// materialization operation. Each input is one observed execution, never
    /// a different phase of the same execution. Requires at least 20 runs so
    /// nearest-rank P95 has more than a single extreme observation.
    /// </summary>
    public static Summary FromRepeatedMaterializationRuns(
        IEnumerable<V9PhysicalSqlMeasuredTimings> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var values = runs.ToArray();
        if (values.Length < 20)
            throw new InvalidDataException("P95/P99 físicos exigem pelo menos 20 execuções comparáveis.");
        if (values.Any(x => x is null
                || !double.IsFinite(x.CreateTablesMilliseconds) || x.CreateTablesMilliseconds < 0
                || !double.IsFinite(x.BulkObservationsMilliseconds) || x.BulkObservationsMilliseconds < 0
                || !double.IsFinite(x.BulkSignaturesMilliseconds) || x.BulkSignaturesMilliseconds < 0
                || !double.IsFinite(x.MaterializePairsMilliseconds) || x.MaterializePairsMilliseconds < 0
                || !double.IsFinite(x.ReadPairsMilliseconds) || x.ReadPairsMilliseconds < 0))
            throw new InvalidDataException("Todas as fases SQL medidas devem ser finitas e não negativas.");
        return FromMeasuredMilliseconds(values.Select(x => x.MaterializePairsMilliseconds));
    }

    /// <summary>
    /// A physical SQL run with the immutable experiment and execution fingerprint.
    /// Quantiles from different candidate universes, SQL rulesets or environments
    /// must never be pooled into a single descriptive estimate.
    /// </summary>
    public sealed record ProvenancedRun(
        string FrameFingerprint,
        string SqlRulesetFingerprint,
        string EnvironmentFingerprint,
        V9PhysicalSqlMeasuredTimings Timings);

    public static Summary FromComparableMaterializationRuns(IEnumerable<ProvenancedRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var values = runs.ToArray();
        if (values.Length < 20
            || values.Any(x => x is null
                || string.IsNullOrWhiteSpace(x.FrameFingerprint)
                || string.IsNullOrWhiteSpace(x.SqlRulesetFingerprint)
                || string.IsNullOrWhiteSpace(x.EnvironmentFingerprint)
                || x.Timings is null))
            throw new InvalidDataException("P95/P99 exigem 20 execuções com proveniência SQL completa.");

        var first = values[0];
        if (values.Any(x =>
                !StringComparer.Ordinal.Equals(x.FrameFingerprint, first.FrameFingerprint)
                || !StringComparer.Ordinal.Equals(x.SqlRulesetFingerprint, first.SqlRulesetFingerprint)
                || !StringComparer.Ordinal.Equals(x.EnvironmentFingerprint, first.EnvironmentFingerprint)))
            throw new InvalidDataException("Execuções de frames, rulesets ou ambientes diferentes não são comparáveis.");

        return FromRepeatedMaterializationRuns(values.Select(x => x.Timings));
    }

}
