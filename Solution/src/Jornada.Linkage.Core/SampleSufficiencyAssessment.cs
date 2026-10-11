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
        if (minimumM < 1) throw new ArgumentOutOfRangeException(nameof(minimumM));
        if (minimumU < 1) throw new ArgumentOutOfRangeException(nameof(minimumU));
        if (minimumIndependentGroups < 1) throw new ArgumentOutOfRangeException(nameof(minimumIndependentGroups));
        // A malformed policy scope must never certify a sample under a different
        // stratum spelling or count a duplicated required stratum as independent.
        if (strata.Count == 0 || strata.Any(s => string.IsNullOrWhiteSpace(s) ||
            !string.Equals(s, s.Trim(), StringComparison.Ordinal)) ||
            strata.Distinct(StringComparer.Ordinal).Count() != strata.Count)
            throw new ArgumentException("Required strata must be unique, nonempty and canonical.", nameof(strata));

        var requiredStrata = strata.ToHashSet(StringComparer.Ordinal);
        // Group the independent pairs once so a streaming source cannot change between
        // eligibility counts and contradiction checks.
        // Normalize surrounding whitespace before deduplication: the same pair or
        // group must never inflate effective sample size through formatting alone.
        // Independent evidence with an unassigned stratum or a whitespace-variant
        // of a requested stratum cannot disappear from the denominator silently.
        // Evidence from unrelated strata remains outside this assessment.
        var relevantEvidence = evidence.Where(e => e.IndependentTruth &&
            (string.IsNullOrWhiteSpace(e.Stratum) ||
             requiredStrata.Contains(e.Stratum) ||
             requiredStrata.Contains(e.Stratum.Trim()))).ToArray();
        var malformedStrata = relevantEvidence.Any(e => !requiredStrata.Contains(e.Stratum));
        var independentEvidence = relevantEvidence.Where(e => requiredStrata.Contains(e.Stratum)).ToArray();
        var missingPairKeyStrata = independentEvidence.Where(e => string.IsNullOrWhiteSpace(e.PairKey))
            .Select(e => e.Stratum).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var independentPairs = independentEvidence.Where(e => !string.IsNullOrWhiteSpace(e.PairKey))
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
        // Missing versus present provenance, or two different provenance keys, for one pair is
        // an integrity error, not merely an undersized sample. Never certify it.
        var provenanceConflicts = independentPairs
            .Where(g => g.Select(e => e.IndependentGroupKey)
                .Distinct(StringComparer.Ordinal).Skip(1).Any())
            .Select(g => g.Key.Stratum)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        // A pair identifier is not an independent sampling group. Treat a group
        // copied from PairKey as invalid provenance, even if numeric minima pass.
        // This guard does not replace independent derivation in the real pipeline.
        var selfDerivedGroups = independentPairs
            .Where(g => g.Any(e => string.Equals(e.IndependentGroupKey, e.PairKey, StringComparison.Ordinal)))
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
            // Numeric pair counts alone are not effective independent sample sizes.
            // The group floor must meet both the policy's diversity requirement and
            // the class-specific sample minimum, without cross-class compensation.
            .Where(g => g.Missing || g.MGroups < Math.Max(minimumIndependentGroups, minimumM)
                || g.UGroups < Math.Max(minimumIndependentGroups, minimumU))
            .Select(g => $"Stratum {g.Stratum}: independent groups m={g.MGroups}, u={g.UGroups}; required m={Math.Max(minimumIndependentGroups, minimumM)}, u={Math.Max(minimumIndependentGroups, minimumU)}; missing group identifiers block certification.")
            .ToArray();
        if (contradictions.Length > 0 || provenanceConflicts.Length > 0 || missingPairKeyStrata.Length > 0 || selfDerivedGroups.Length > 0 || malformedStrata)
            return new Result(Status.Indeterminate, counts,
                reasons.Concat(groupReasons)
                    .Concat(contradictions.Select(s => $"Stratum {s}: contradictory independent labels block certification; investigate source integrity."))
                    .Concat(provenanceConflicts.Select(s => $"Stratum {s}: contradictory independent group provenance blocks certification; investigate source integrity."))
                    .Concat(missingPairKeyStrata.Select(s => $"Stratum {s}: missing independent pair key blocks certification; investigate source integrity."))
                    .Concat(selfDerivedGroups.Select(s => $"Stratum {s}: independent group identifier equals pair key; provenance is not independent."))
                    .Concat(malformedStrata ? new[] { "Independent evidence has a missing or noncanonical required stratum; certification blocked." } : Array.Empty<string>())
                    .ToArray());
        return new Result(eligible.Length == 0 ? Status.Indeterminate :
            reasons.Length == 0 && groupReasons.Length == 0 ? Status.Sufficient : Status.Insufficient, counts, reasons.Concat(groupReasons).ToArray());
    }
}
