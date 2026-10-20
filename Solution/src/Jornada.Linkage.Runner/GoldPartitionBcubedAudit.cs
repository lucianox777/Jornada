namespace Jornada.Linkage.Runner;

/// <summary>
/// Post-publication, truth-labeled B-cubed diagnostic for an incremental Gold
/// partition. Never used by blocking, FS ranking or publication decisions.
/// It exports aggregates only, without source IDs, true labels or UUIDs.
/// </summary>
public static class GoldPartitionBcubedAudit
{
    public const string Version = "GOLD_BCUBED_STANDARD_AND_COVERAGE_AWARE_V1";

    public sealed record Metrics(
        string ContractVersion,
        long Sources,
        long PublishedSources,
        long PendingSources,
        long TrueIdentityGroups,
        long GoldIdentityGroups,
        double PendingSourceRate,
        double StandardPrecision,
        double StandardRecall,
        double StandardF1,
        double CoverageAwarePrecision,
        double CoverageAwareRecall,
        double CoverageAwareF1,
        double? PublishedOnlyPrecision,
        double? PublishedOnlyRecall);

    /// <summary>
    /// Standard B-cubed treats every unassigned observation as its own
    /// singleton cluster; that cluster counts itself as one correct member.
    /// This may reward ABSTENTION. CoverageAware instead gives every pending
    /// observation P=R=0, keeping ALL observations in both denominators.
    /// PublishedOnly is diagnostic and deliberately changes the denominator.
    /// Never interpret a standard singleton score as Gold publication coverage.
    /// </summary>
    public static Metrics Measure(
        IReadOnlyDictionary<long, string> truthBySource,
        IReadOnlyDictionary<long, Guid> goldBySource)
    {
        ArgumentNullException.ThrowIfNull(truthBySource);
        ArgumentNullException.ThrowIfNull(goldBySource);
        if (truthBySource.Count == 0 || truthBySource.Values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Gabarito integral e não vazio é obrigatório.");
        foreach (var (sourceId, uuid) in goldBySource)
        {
            if (uuid == Guid.Empty || !truthBySource.ContainsKey(sourceId))
                throw new InvalidDataException("Gold inclui fonte ou UUID inexistente no gabarito.");
        }

        var truthSizes = truthBySource.GroupBy(x => x.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.LongCount(), StringComparer.Ordinal);
        var goldClusters = goldBySource.GroupBy(x => x.Value)
            .ToDictionary(g => g.Key, g =>
            {
                var labels = g.GroupBy(x => truthBySource[x.Key], StringComparer.Ordinal)
                    .ToDictionary(t => t.Key, t => t.LongCount(), StringComparer.Ordinal);
                return (Size: g.LongCount(), Labels: labels);
            });

        double standardP = 0, standardR = 0, coveredP = 0, coveredR = 0;
        foreach (var (sourceId, truth) in truthBySource)
        {
            var trueSize = truthSizes[truth];
            if (!goldBySource.TryGetValue(sourceId, out var uuid))
            {
                standardP += 1d;
                standardR += 1d / trueSize;
                continue;
            }
            var cluster = goldClusters[uuid];
            var sameTruth = cluster.Labels[truth];
            var precision = (double)sameTruth / cluster.Size;
            var recall = (double)sameTruth / trueSize;
            standardP += precision;
            standardR += recall;
            coveredP += precision;
            coveredR += recall;
        }

        var count = (double)truthBySource.Count;
        var assigned = (double)goldBySource.Count;
        var p = standardP / count;
        var r = standardR / count;
        var cp = coveredP / count;
        var cr = coveredR / count;
        return new(
            ContractVersion: Version,
            Sources: truthBySource.Count,
            PublishedSources: goldBySource.Count,
            PendingSources: truthBySource.Count - goldBySource.Count,
            TrueIdentityGroups: truthSizes.Count,
            GoldIdentityGroups: goldClusters.Count,
            PendingSourceRate: (count - assigned) / count,
            StandardPrecision: p,
            StandardRecall: r,
            StandardF1: Harmonic(p, r),
            CoverageAwarePrecision: cp,
            CoverageAwareRecall: cr,
            CoverageAwareF1: Harmonic(cp, cr),
            PublishedOnlyPrecision: assigned == 0 ? null : coveredP / assigned,
            PublishedOnlyRecall: assigned == 0 ? null : coveredR / assigned);
    }

    private static double Harmonic(double p, double r) =>
        p + r == 0 ? 0 : 2d * p * r / (p + r);
}
