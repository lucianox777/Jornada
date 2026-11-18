using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Pure, nonpublishing integrity contract for a complete operational V9 cohort.
/// Both expected census and consumed graph must come from independently verified
/// sources; this class does not retrieve SQL data or certify production readiness.
/// </summary>
public static class V9OperationalCandidateGraphIntegrity
{
    public sealed record Census(long ObservationCount, long EdgeCount, string CanonicalEdgesSha256);
    public sealed record Verified(long ObservationCount, long EdgeCount, string CanonicalEdgesSha256);

    public static Verified Verify(
        IReadOnlyCollection<long> frozenObservationIds,
        IEnumerable<(long A, long B)> consumedEdges,
        Census independentSourceCensus)
    {
        ArgumentNullException.ThrowIfNull(frozenObservationIds);
        ArgumentNullException.ThrowIfNull(consumedEdges);
        ArgumentNullException.ThrowIfNull(independentSourceCensus);
        if (independentSourceCensus.ObservationCount < 0
            || independentSourceCensus.EdgeCount < 0
            || independentSourceCensus.CanonicalEdgesSha256.Length != 64
            || independentSourceCensus.CanonicalEdgesSha256.Any(c =>
                !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))))
            throw new InvalidDataException("RUNNER_V9_GRAPH_CENSUS_INVALID");

        var ids = frozenObservationIds.ToHashSet();
        if (ids.Count != frozenObservationIds.Count
            || ids.Any(id => id <= 0)
            || ids.Count != independentSourceCensus.ObservationCount)
            throw new InvalidDataException("RUNNER_V9_GRAPH_COHORT_MISMATCH");

        // No page cap, sampling, silent truncation or per-batch deduplication.
        var edges = new HashSet<(long A, long B)>();
        foreach (var (a, b) in consumedEdges)
        {
            if (a <= 0 || b <= 0 || a >= b || !ids.Contains(a) || !ids.Contains(b))
                throw new InvalidDataException("RUNNER_V9_GRAPH_ORPHAN_OR_INVALID_EDGE");
            if (!edges.Add((a, b)))
                throw new InvalidDataException("RUNNER_V9_GRAPH_DUPLICATE_EDGE");
            if (edges.Count > independentSourceCensus.EdgeCount)
                throw new InvalidDataException("RUNNER_V9_GRAPH_EDGE_COUNT_MISMATCH");
        }

        if (edges.Count != independentSourceCensus.EdgeCount)
            throw new InvalidDataException("RUNNER_V9_GRAPH_EDGE_COUNT_MISMATCH");

        // Stable length-delimited canonical edge encoding; independent SQL census
        // must explicitly implement the same specification, not reuse these edges.
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (a, b) in edges.OrderBy(edge => edge.A).ThenBy(edge => edge.B))
        {
            var line = string.Concat(
                a.ToString(CultureInfo.InvariantCulture), ":",
                b.ToString(CultureInfo.InvariantCulture), "\n");
            var bytes = Encoding.UTF8.GetBytes(line);
            sha.AppendData(bytes);
        }
        var digest = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(digest, independentSourceCensus.CanonicalEdgesSha256,
                StringComparison.Ordinal))
            throw new InvalidDataException("RUNNER_V9_GRAPH_HASH_MISMATCH");
        return new Verified(ids.Count, edges.Count, digest);
    }
}
