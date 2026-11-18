using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Fail-closed bridge between the nonpublishing cohort preview and a separately
/// sourced operational graph census. Does not produce an independent census.
/// </summary>
public static class V9OperationalGraphReceiptVerification
{
    public static V9OperationalCandidateGraphIntegrity.Verified Verify(
        IReadOnlyCollection<V9OperationalCohortGraphPreview.Source> frozenCohort,
        V9OperationalCohortGraphPreview.Receipt preview,
        V9OperationalCandidateGraphIntegrity.Census independentSourceCensus)
    {
        ArgumentNullException.ThrowIfNull(frozenCohort);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(independentSourceCensus);
        if (preview.Edges is null || preview.CanonicalEdgesSha256 is null
            || preview.ObservationCount != frozenCohort.Count
            || preview.UndirectedEdgeCount != preview.Edges.Count
            || preview.DirectedCandidateCount != checked(preview.UndirectedEdgeCount * 2))
            throw new InvalidDataException("RUNNER_V9_GRAPH_PREVIEW_RECEIPT_MISMATCH");

        // Verify the preview's own receipt before comparing to the separately
        // generated source census: neither count nor hash may be trusted alone.
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (a, b) in preview.Edges.OrderBy(e => e.A).ThenBy(e => e.B))
            sha.AppendData(Encoding.UTF8.GetBytes(
                a.ToString(CultureInfo.InvariantCulture) + ":" +
                b.ToString(CultureInfo.InvariantCulture) + "\n"));
        var digest = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(digest, preview.CanonicalEdgesSha256, StringComparison.Ordinal))
            throw new InvalidDataException("RUNNER_V9_GRAPH_PREVIEW_HASH_MISMATCH");

        var ids = frozenCohort.Select(s => s is null ? 0 : s.ObservationId).ToArray();
        return V9OperationalCandidateGraphIntegrity.Verify(
            ids, preview.Edges, independentSourceCensus);
    }
}
