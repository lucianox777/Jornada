using System.Globalization;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// OPERATIONAL grouping only: a complete frozen SQL/FS frame can evaluate
/// several conflict margins from the same TRAIN fit and candidate scores.
/// Request/option hashes, CP multiplicity and inference remain unchanged.
/// </summary>
public sealed record V9MarginFrameWorkKey(
    string PhysicalSqlFrameKey, string OptionId, decimal Margin,
    bool PositiveTf, int UnpublishedTermPolicy);

public static class V9MarginFrameWorkPartition
{
    /// <summary>
    /// Return original indices, grouped by immutable SQL frame, TF coverage
    /// behavior, exactly matching margin subsets and <=64 DISTINCT options
    /// by <=8 margins per probe. Never add a Cartesian-cell not in input.
    /// Original request identities and order are preserved as indices.
    /// </summary>
    public static IReadOnlyList<int[]> Group(
        IReadOnlyList<V9MarginFrameWorkKey> keys,
        int maximumOptions = 64, int maximumMargins = 8)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (maximumOptions is < 1 or > 64 || maximumMargins is < 1 or > 8)
            throw new InvalidDataException("V9_MARGIN_REUSE_GROUP_INVALID_BOUNDS");
        if (keys.Any(x => x is null || string.IsNullOrWhiteSpace(x.PhysicalSqlFrameKey)
            || string.IsNullOrWhiteSpace(x.OptionId) || x.Margin is < 0 or > 1000))
            throw new InvalidDataException("V9_MARGIN_REUSE_GROUP_INVALID_REQUEST");

        var indexed = keys.Select((key, index) => (Key: key, Index: index)).ToArray();
        var grouped = new List<int[]>();
        foreach (var frame in indexed.GroupBy(x => (
                x.Key.PhysicalSqlFrameKey, x.Key.PositiveTf, x.Key.UnpublishedTermPolicy))
            .OrderBy(x => x.Key.PhysicalSqlFrameKey, StringComparer.Ordinal)
            .ThenBy(x => x.Key.PositiveTf)
            .ThenBy(x => x.Key.UnpublishedTermPolicy))
        {
            // Keep every option's set of margins together when possible, but
            // respect the existing hard 64-option and 8-margin probe contracts.
            var perOption = frame.GroupBy(x => x.Key.OptionId, StringComparer.Ordinal)
                .OrderBy(x => x.Key, StringComparer.Ordinal);
            var optionSlices = new List<(string OptionId, string Signature, (V9MarginFrameWorkKey Key, int Index)[] Requests)>();
            foreach (var option in perOption)
            {
                var canonical = option.OrderBy(x => x.Key.Margin).ToArray();
                if (canonical.Select(x => x.Key.Margin).Distinct().Count() != canonical.Length)
                    throw new InvalidDataException("V9_MARGIN_REUSE_GROUP_DUPLICATE_OPTION_MARGIN");
                foreach (var slice in canonical.Chunk(maximumMargins))
                {
                    // Explicit canonical decimal representation; never use a
                    // culture-dependent string nor infer missing margin cells.
                    var signature = string.Join(";", slice.Select(x =>
                        x.Key.Margin.ToString("G29", CultureInfo.InvariantCulture)));
                    optionSlices.Add((option.Key, signature, slice));
                }
            }
            foreach (var signatureGroup in optionSlices.GroupBy(x => x.Signature, StringComparer.Ordinal)
                .OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                foreach (var optionsChunk in signatureGroup
                    .OrderBy(x => x.OptionId, StringComparer.Ordinal).Chunk(maximumOptions))
                {
                    var requests = optionsChunk.SelectMany(x => x.Requests.Select(y => y.Index)).ToArray();
                    if (requests.Length == 0)
                        throw new InvalidDataException("V9_MARGIN_REUSE_GROUP_EMPTY");
                    grouped.Add(requests);
                }
            }
        }
        var indices = grouped.SelectMany(x => x).ToArray();
        if (indices.Length != keys.Count || indices.Distinct().Count() != keys.Count
            || indices.Any(i => i < 0 || i >= keys.Count))
            throw new InvalidDataException("V9_MARGIN_REUSE_GROUP_INCOMPLETE_OR_DUPLICATE");
        return Array.AsReadOnly(grouped.ToArray());
    }
}
