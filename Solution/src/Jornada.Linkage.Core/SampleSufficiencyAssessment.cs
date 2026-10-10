namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Pure assessment only: never invokes calibration or model activation.</summary>
public static class SampleSufficiencyAssessment
{
    public enum Status { Indeterminate, Insufficient, Sufficient }
    public sealed record Evidence(string Stratum, string PairKey, bool IndependentTruth, bool Match, string? IndependentGroupKey = null);
    public sealed record Count(string Stratum, int M, int U);
    public sealed record Result(Status State, IReadOnlyList<Count> Counts, IReadOnlyList<string> Reasons);

    public static Result Evaluate(IEnumerable<Evidence> evidence, IReadOnlyCollection<string> strata, int minimumM, int minimumU, int minimumIndependentGroups = 1)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(strata);
        if (minimumM < 1 || minimumU < 1 || minimumIndependentGroups < 1) throw new ArgumentOutOfRangeException(nameof(minimumM));
        if (strata.Count == 0 || strata.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one stratum is required.", nameof(strata));

        var requiredStrata = strata.ToHashSet(StringComparer.Ordinal);
        // Group the independent pairs once so a streaming source cannot change between
        // eligibility counts and contradiction checks.
        // Normalize surrounding whitespace before deduplication: the same pair or
        // group must never inflate effective sample size through formatting alone.
        var independentPairs = evidence.Where(e => requiredStrata.Contains(e.Stratum) && e.IndependentTruth
                && !string.IsNullOrWhiteSpace(e.PairKey))
            .Select(e => e with {
                PairKey = e.PairKey.Trim(),
                IndependentGroupKey = e.IndependentGroupKey?.Trim()
            })
            .GroupBy(e => (e.Stratum, e.PairKey)).ToArray();
        var eligible = independentPairs
            .Where(g => g.Select(e => e.Match).Distinct().Count() == 1)
            .Select(g => {
                var first = g.First();
                var keys = g.Select(e => e.IndependentGroupKey).Distinct(StringComparer.Ordinal).ToArray();
                return first with { IndependentGroupKey = keys.Length == 1 ? keys[0] : null };
            }).ToArray();
        // Contradictory independent truth in a required stratum blocks certification.
        var contradictions = independentPairs
            .Where(g => g.Select(e => e.Match).Distinct().Count() > 1)
            .Select(g => g.Key.Stratum)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var counts = strata.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal)
            .Select(s => new Count(s, eligible.Count(e => e.Stratum == s && e.Match),
                eligible.Count(e => e.Stratum == s && !e.Match))).ToArray();
        var reasons = counts.Where(c => c.M < minimumM || c.U < minimumU)
            .Select(c => $"Stratum {c.Stratum}: m={c.M}, u={c.U}; minimum m={minimumM}, u={minimumU}.")
            .ToArray();
        // Require independent diversity in m and u separately for each stratum.
        var groupReasons = counts.Select(c => {
            var stratumEvidence = eligible.Where(e => e.Stratum == c.Stratum).ToArray();
            var mGroups = stratumEvidence.Where(e => e.Match)
                .Select(e => e.IndependentGroupKey).Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.Ordinal).Count();
            var uGroups = stratumEvidence.Where(e => !e.Match)
                .Select(e => e.IndependentGroupKey).Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.Ordinal).Count();
            var missing = stratumEvidence.Any(e => string.IsNullOrWhiteSpace(e.IndependentGroupKey));
            return new { c.Stratum, MGroups = mGroups, UGroups = uGroups, Missing = missing };
        })
            .Where(g => g.Missing || g.MGroups < minimumIndependentGroups || g.UGroups < minimumIndependentGroups)
            .Select(g => $"Stratum {g.Stratum}: independent groups m={g.MGroups}, u={g.UGroups}; required per class={minimumIndependentGroups}; missing group identifiers block certification.")
            .ToArray();
        if (contradictions.Length > 0)
            return new Result(Status.Indeterminate, counts,
                reasons.Concat(groupReasons).Concat(contradictions.Select(s => $"Stratum {s}: contradictory independent labels block certification; investigate source integrity.")).ToArray());
        return new Result(eligible.Length == 0 ? Status.Indeterminate :
            reasons.Length == 0 && groupReasons.Length == 0 ? Status.Sufficient : Status.Insufficient, counts, reasons.Concat(groupReasons).ToArray());
    }
}
