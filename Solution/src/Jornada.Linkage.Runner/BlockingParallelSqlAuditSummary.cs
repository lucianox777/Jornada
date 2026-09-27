namespace Jornada.Linkage.Runner;

/// <summary>Aggregates only anonymized per-observation D/C/union SQL measurements.</summary>
internal static class BlockingParallelSqlAuditMetrics
{
    internal const string MethodVersion = "BLOCKING_PARALLEL_SQL_AUDIT_V1";

    internal static BlockingParallelSqlAuditSummary Summarize(
        IReadOnlyList<BlockingParallelSqlAuditRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var d = rows.Select(static r => r.DynamicLatencyMs).Order().ToArray();
        var c = rows.Where(static r => r.CombinedEligible)
            .Select(static r => r.CombinedLatencyMs).Order().ToArray();
        var u = rows.Select(static r => r.TaggedUnionLatencyMs).Order().ToArray();
        foreach (var row in rows)
        {
            if (row.DynamicCandidateCount < 0 || row.CombinedCandidateCount < 0
                || row.UnionCandidateCount < 0 || row.SharedCandidateCount < 0
                || row.UnionCandidateCount !=
                    row.DynamicCandidateCount + row.CombinedCandidateCount - row.SharedCandidateCount
                || row.SharedCandidateCount > Math.Min(row.DynamicCandidateCount, row.CombinedCandidateCount)
                || (!row.CombinedEligible && (row.CombinedCandidateCount != 0 || row.TruthInCombined))
                || row.DynamicLatencyMs < 0 || row.CombinedLatencyMs < 0 || row.TaggedUnionLatencyMs < 0)
            {
                throw new InvalidDataException("Diagnóstico paralelo SQL contém contagens ou elegibilidade inconsistentes.");
            }
        }

        var dynamicTruth = rows.Count(static r => r.TruthInDynamic);
        var combinedTruth = rows.Count(static r => r.TruthInCombined);
        var unionTruth = rows.Count(static r => r.TruthInDynamic || r.TruthInCombined);
        return new BlockingParallelSqlAuditSummary(
            MethodVersion, rows.Count,
            rows.Count(static r => r.CombinedEligible),
            rows.Sum(static r => r.DynamicCandidateCount),
            rows.Sum(static r => r.CombinedCandidateCount),
            rows.Sum(static r => r.UnionCandidateCount),
            rows.Sum(static r => r.SharedCandidateCount),
            dynamicTruth, combinedTruth, unionTruth,
            rows.Count(static r => r.TruthInDynamic && r.TruthInCombined),
            rows.Count(static r => r.TruthInDynamic && !r.TruthInCombined),
            rows.Count(static r => !r.TruthInDynamic && r.TruthInCombined),
            Percent(dynamicTruth, rows.Count),
            Percent(combinedTruth, rows.Count),
            Percent(combinedTruth, rows.Count(static r => r.CombinedEligible)),
            Percent(unionTruth, rows.Count),
            Percentile(d, 0.95),
            Percentile(c, 0.95),
            Percentile(u, 0.95),
            d.Length == 0 ? 0 : d[^1],
            c.Length == 0 ? 0 : c[^1],
            u.Length == 0 ? 0 : u[^1]);
    }

    private static decimal Percent(int part, int total)
        => total == 0 ? 0m : decimal.Round(100m * part / total, 4);

    private static long Percentile(long[] sorted, double probability)
        => sorted.Length == 0 ? 0
            : sorted[Math.Clamp((int)Math.Ceiling(probability * sorted.Length) - 1, 0, sorted.Length - 1)];
}

internal sealed record BlockingParallelSqlAuditRow(
    bool CombinedEligible,
    long DynamicCandidateCount,
    long CombinedCandidateCount,
    long UnionCandidateCount,
    long SharedCandidateCount,
    bool TruthInDynamic,
    bool TruthInCombined,
    long DynamicLatencyMs,
    long CombinedLatencyMs,
    long TaggedUnionLatencyMs);

internal sealed record BlockingParallelSqlAuditSummary(
    string MethodVersion,
    int SampleSize,
    int CombinedEligibleObservations,
    long DynamicCandidatePairs,
    long CombinedCandidatePairs,
    long UnionCandidatePairs,
    long SharedCandidatePairs,
    int DynamicTruthRecovered,
    int CombinedTruthRecovered,
    int UnionTruthRecovered,
    int SharedTruthRecovered,
    int DynamicOnlyTruthRecovered,
    int CombinedOnlyTruthRecovered,
    decimal DynamicRecallPct,
    decimal CombinedRecallPct,
    decimal CombinedConditionalRecallPct,
    decimal UnionRecallPct,
    long DynamicLatencyP95Ms,
    long CombinedLatencyP95Ms,
    long TaggedUnionLatencyP95Ms,
    long DynamicLatencyMaxMs,
    long CombinedLatencyMaxMs,
    long TaggedUnionLatencyMaxMs);
