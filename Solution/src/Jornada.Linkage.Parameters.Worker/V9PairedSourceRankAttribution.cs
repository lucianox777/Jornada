using System.Collections.ObjectModel;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Paired source-level Rank/Resolve transitions in memory, reduced to
/// non-identifying counts before returning. A U fallback association is
/// DESCRIPTIVE; it does not by itself prove a causal score contribution.
/// </summary>
public sealed record V9PairedSourceRankReport(
    string Scope,
    int SourceCount,
    long V2CorrectResolved, long V3CorrectResolved,
    long V2IncorrectResolved, long V3IncorrectResolved,
    long V2CorrectLostToV3Conflict, long V2CorrectLostToV3Unresolved,
    long V2CorrectLostToV3IncorrectResolved, long V3CorrectGained,
    long V2CorrectLostWithV3UFallback, long V2CorrectLostWithoutV3UFallback,
    long V2CorrectRetainedWithV3UFallback,
    long V2CorrectLostSameTopTwoGroups,
    long V2CorrectLostChangedTopCandidate,
    long V2CorrectLostMarginHalfCrossedWithStableTopTwo,
    long DifferentFirstCandidate, long DifferentSecondCandidate,
    long DifferentBlindGroupMembership,
    long ComparableTopTwoWithBothScores,
    IReadOnlyDictionary<string, long> V3ReasonsOnV2CorrectLoss,
    IReadOnlyDictionary<string, long> ChangeInTopTwoGapBins,
    bool IncludesIndividualSourceData = false,
    bool UFallbackCausalityEstablished = false,
    bool IndependentValidation = false,
    bool TestUsed = false,
    bool Certified = false);

public static class V9PairedSourceRankAttribution
{
    private const string Scope = "SAME_SQL_SYNTHETIC_TRAIN_SELECTION_PAIRED_SOURCE_DIAGNOSTIC";
    private static readonly string[] Bins =
    [
        "LESS_THAN_MINUS_2", "MINUS_2_TO_MINUS_0.5",
        "MINUS_0.5_TO_MINUS_0.25", "MINUS_0.25_TO_0",
        "0_TO_0.25", "0.25_TO_0.5", "0.5_TO_2", "2_AND_ABOVE"
    ];

    public static V9PairedSourceRankReport Compare(
        V9PhysicalSourceResolveMetric v2, V9PhysicalSourceResolveMetric v3,
        IReadOnlySet<string> v3FallbackUCells)
    {
        ArgumentNullException.ThrowIfNull(v2);
        ArgumentNullException.ThrowIfNull(v3);
        ArgumentNullException.ThrowIfNull(v3FallbackUCells);
        if (v2.CandidateGrouping != "BLIND_FS_COMPLETE_LINK_CANDIDATES"
            || v2.CandidateGrouping != v3.CandidateGrouping
            || v2.ConflictMargin != v3.ConflictMargin
            || v2.AllValidationObservations != v3.AllValidationObservations
            || v2.EligibleSourceObservationsBeforeBlocking != v3.EligibleSourceObservationsBeforeBlocking
            || v2.EligibleSourceObservationsWithTrueSqlCandidate != v3.EligibleSourceObservationsWithTrueSqlCandidate
            || v2.SourceRankInternal is null || v3.SourceRankInternal is null
            || v2.SourceRankInternal.Count != v2.AllValidationObservations
            || v3.SourceRankInternal.Count != v3.AllValidationObservations
            || v3FallbackUCells.Any(c => !System.Text.RegularExpressions.Regex.IsMatch(c, @"^[0-8]/[0-8]$")))
            throw new InvalidDataException("Rank evidence requires identical complete blinded SQL cohort.");
        var left = v2.SourceRankInternal.ToDictionary(x => x.SourceObservationId);
        var right = v3.SourceRankInternal.ToDictionary(x => x.SourceObservationId);
        if (!left.Keys.ToHashSet().SetEquals(right.Keys)
            || left.Keys.Any(id => id <= 0))
            throw new InvalidDataException("Unmatched source IDs in paired V2/V3 scoring.");

        long ltp = 0, rtp = 0, lfp = 0, rfp = 0;
        long toConflict = 0, toUnresolved = 0, toWrong = 0, gains = 0;
        long fallbackLoss = 0, noFallbackLoss = 0, fallbackRetained = 0;
        long stableLost = 0, firstChangedLost = 0, gapCrossed = 0;
        long changedFirst = 0, changedSecond = 0, changedGroup = 0, compared = 0;
        var reasons = new Dictionary<string, long>(StringComparer.Ordinal);
        var bins = Bins.ToDictionary(x => x, _ => 0L, StringComparer.Ordinal);
        foreach (var id in left.Keys.OrderBy(x => x))
        {
            var a = left[id]; var b = right[id];
            Validate(a); Validate(b);
            bool aCorrect = a.ResolveStatus == "RESOLVIDO" && a.FirstCorrect is true;
            bool bCorrect = b.ResolveStatus == "RESOLVIDO" && b.FirstCorrect is true;
            bool aWrong = a.ResolveStatus == "RESOLVIDO" && a.FirstCorrect is false;
            bool bWrong = b.ResolveStatus == "RESOLVIDO" && b.FirstCorrect is false;
            if (aCorrect) ltp++;
            if (bCorrect) rtp++;
            if (aWrong) lfp++;
            if (bWrong) rfp++;
            if (bCorrect && !aCorrect) gains++;
            bool firstSame = a.FirstObservationId == b.FirstObservationId;
            bool secondSame = a.SecondObservationId == b.SecondObservationId;
            bool groupSame = a.FirstGroupSignature == b.FirstGroupSignature
                && a.SecondGroupSignature == b.SecondGroupSignature;
            if (!firstSame) changedFirst++;
            if (!secondSame) changedSecond++;
            if (!groupSame) changedGroup++;
            var v3Fallback = (b.FirstCell is not null && v3FallbackUCells.Contains(b.FirstCell))
                || (b.SecondCell is not null && v3FallbackUCells.Contains(b.SecondCell));
            if (aCorrect && bCorrect && v3Fallback) fallbackRetained++;
            if (!aCorrect || bCorrect) continue;
            reasons[b.ResolveReason] = checked(reasons.GetValueOrDefault(b.ResolveReason) + 1);
            if (b.ResolveStatus == "CONFLITO") toConflict++;
            else if (b.ResolveStatus == "NAO_RESOLVIDO") toUnresolved++;
            else if (bWrong) toWrong++;
            else throw new InvalidDataException("Lost correct resolution has unknown successor.");
            if (v3Fallback) fallbackLoss++; else noFallbackLoss++;
            if (!firstSame) firstChangedLost++;
            if (firstSame && secondSame && groupSame) stableLost++;
            if (a.FirstLogOdds is { } a1 && a.SecondLogOdds is { } a2
                && b.FirstLogOdds is { } b1 && b.SecondLogOdds is { } b2)
            {
                // Paired gap shift: SAME source and candidate population in memory.
                // Exact score decomposition to M/U/TF remains a separate gate.
                var delta = (b1 - b2) - (a1 - a2);
                string bin = delta < -2m ? Bins[0] : delta < -.5m ? Bins[1]
                    : delta < -.25m ? Bins[2] : delta < 0m ? Bins[3]
                    : delta < .25m ? Bins[4] : delta < .5m ? Bins[5]
                    : delta < 2m ? Bins[6] : Bins[7];
                bins[bin] = checked(bins[bin] + 1);
                compared++;
                if (firstSame && secondSame && groupSame
                    && a1 - a2 >= .5m && b1 - b2 < .5m) gapCrossed++;
            }
        }
        if (ltp != v2.TrueAcceptedSourceObservations
            || rtp != v3.TrueAcceptedSourceObservations
            || lfp != v2.FalseAcceptedSourceObservations
            || rfp != v3.FalseAcceptedSourceObservations
            || ltp - rtp != toConflict + toUnresolved + toWrong - gains
            || fallbackLoss + noFallbackLoss != toConflict + toUnresolved + toWrong
            || compared != bins.Values.Sum())
            throw new InvalidDataException("Paired Rank statuses disagree with physical TP/FP or aggregate bins.");
        return new V9PairedSourceRankReport(
            Scope, left.Count, ltp, rtp, lfp, rfp,
            toConflict, toUnresolved, toWrong, gains,
            fallbackLoss, noFallbackLoss, fallbackRetained,
            stableLost, firstChangedLost, gapCrossed,
            changedFirst, changedSecond, changedGroup, compared,
            new ReadOnlyDictionary<string, long>(reasons.OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)),
            new ReadOnlyDictionary<string, long>(bins));
    }

    private static void Validate(V9SourceTopTwoDiagnostic x)
    {
        if (x.SourceObservationId <= 0
            || x.ResolveStatus is not ("RESOLVIDO" or "NAO_RESOLVIDO" or "CONFLITO")
            || string.IsNullOrWhiteSpace(x.ResolveReason)
            || (x.FirstObservationId is null) != (x.FirstLogOdds is null)
            || (x.SecondObservationId is null) != (x.SecondLogOdds is null)
            || (x.FirstObservationId is null) != (x.FirstCell is null)
            || (x.SecondObservationId is null) != (x.SecondCell is null)
            || (x.FirstObservationId is null) != (x.FirstCorrect is null)
            || (x.SecondObservationId is null) != (x.SecondCorrect is null)
            || (x.FirstObservationId is null) != (x.FirstGroupSignature is null)
            || (x.SecondObservationId is null) != (x.SecondGroupSignature is null)
            || x.SecondObservationId is not null && x.FirstObservationId is null
            || x.FirstLogOdds is { } a && x.SecondLogOdds is { } b && a < b)
            throw new InvalidDataException("Inconsistent private source Rank evidence.");
    }
}
