using System.Collections.ObjectModel;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Aggregated, privacy-preserving Rank evidence for known synthetic TRAIN_SELECTION.
/// A single model cannot establish V2-vs-V3 causality: the paired comparison remains
/// a separate study requiring the SAME SQL frame, source IDs, and both frozen fits.
/// No candidate IDs, personal attributes, or source-level scores leave this class.
/// </summary>
public sealed record V9SourceGapAttributionReport(
    string Scope,
    int MinimumCellDisclosureCount,
    long TotalSources,
    long WithoutFirstCandidate,
    long WithoutSecondCandidate,
    IReadOnlyDictionary<string, long> BestMinusSecondLogOddsBins,
    IReadOnlyDictionary<string, long> BestCandidateCells,
    IReadOnlyDictionary<string, long> SecondCandidateCells,
    long SuppressedBestCellObservations,
    long SuppressedSecondCellObservations,
    long BestCandidateCorrect,
    long BestCandidateIncorrect,
    long SecondCandidateCorrect,
    long SecondCandidateIncorrect,
    bool PairedV2V3 = false,
    bool TestUsed = false,
    bool Certified = false);

/// <summary>
/// Aggregate only. A cell with fewer than five source observations is never
/// emitted as its own bucket. Suppressed totals are kept for reconciliation.
/// </summary>
public sealed class V9SourceGapAttributionAccumulator
{
    public const string EvidenceScope = "SYNTHETIC_TRAIN_SELECTION_RANK_ONLY_NOT_CAUSAL";
    public const int CellDisclosureMinimum = 5;
    private static readonly string[] Bins =
    [
        "NEGATIVE", "0_TO_0.25", "0.25_TO_0.5", "0.5_TO_1",
        "1_TO_2", "2_TO_4", "4_AND_ABOVE"
    ];
    private readonly Dictionary<string, long> gapBins =
        Bins.ToDictionary(x => x, _ => 0L, StringComparer.Ordinal);
    private readonly Dictionary<string, long> firstCells = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> secondCells = new(StringComparer.Ordinal);
    private long total;
    private long noFirst;
    private long noSecond;
    private long firstCorrect;
    private long firstIncorrect;
    private long secondCorrect;
    private long secondIncorrect;

    public void Add(decimal? bestLogOdds, decimal? secondLogOdds,
        string? bestCell, string? secondCell, bool? bestCorrect, bool? secondCorrect)
    {
        total = checked(total + 1);
        if (bestLogOdds is null)
        {
            if (secondLogOdds is not null || bestCell is not null || secondCell is not null
                || bestCorrect is not null || secondCorrect is not null)
                throw new InvalidDataException("Gap without best candidate is inconsistent.");
            noFirst = checked(noFirst + 1);
            noSecond = checked(noSecond + 1);
            return;
        }
        if (string.IsNullOrWhiteSpace(bestCell) || bestCorrect is null)
            throw new InvalidDataException("Missing source-level best evidence.");
        AddCell(firstCells, bestCell);
        if (bestCorrect.Value) firstCorrect++; else firstIncorrect++;
        if (secondLogOdds is null)
        {
            if (secondCell is not null || secondCorrect is not null)
                throw new InvalidDataException("Second candidate missing but evidence exists.");
            noSecond = checked(noSecond + 1);
            return;
        }
        if (string.IsNullOrWhiteSpace(secondCell) || secondCorrect is null)
            throw new InvalidDataException("Missing source-level second evidence.");
        AddCell(secondCells, secondCell);
        if (secondCorrect.Value) secondCorrect++; else secondIncorrect++;
        var gap = bestLogOdds.Value - secondLogOdds.Value;
        string bin = gap < 0m ? Bins[0] : gap < .25m ? Bins[1]
            : gap < .5m ? Bins[2] : gap < 1m ? Bins[3]
            : gap < 2m ? Bins[4] : gap < 4m ? Bins[5] : Bins[6];
        gapBins[bin] = checked(gapBins[bin] + 1);
    }

    private static void AddCell(Dictionary<string, long> cells, string key)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(key, @"^[0-8]/[0-8]$"))
            throw new InvalidDataException("Invalid nominal 81-cell ordinal.");
        cells[key] = checked(cells.GetValueOrDefault(key) + 1);
    }

    private static (IReadOnlyDictionary<string, long>, long) Disclose(Dictionary<string, long> input)
    {
        var visible = input.Where(x => x.Value >= CellDisclosureMinimum)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var suppressed = input.Where(x => x.Value < CellDisclosureMinimum).Sum(x => x.Value);
        return (new ReadOnlyDictionary<string, long>(visible), suppressed);
    }

    public V9SourceGapAttributionReport Export()
    {
        var (first, suppressedFirst) = Disclose(firstCells);
        var (second, suppressedSecond) = Disclose(secondCells);
        if (total != noFirst + firstCorrect + firstIncorrect
            || total != noSecond + secondCorrect + secondIncorrect
            || gapBins.Values.Sum() != total - noSecond
            || first.Values.Sum() + suppressedFirst != total - noFirst
            || second.Values.Sum() + suppressedSecond != total - noSecond)
            throw new InvalidDataException("Source gap aggregate reconciliation failed.");
        return new V9SourceGapAttributionReport(
            EvidenceScope, CellDisclosureMinimum, total, noFirst, noSecond,
            new ReadOnlyDictionary<string, long>(gapBins.OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)),
            first, second, suppressedFirst, suppressedSecond, firstCorrect,
            firstIncorrect, secondCorrect, secondIncorrect);
    }
}
