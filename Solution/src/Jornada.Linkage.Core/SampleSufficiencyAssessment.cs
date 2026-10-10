namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Pure assessment only: never invokes calibration or model activation.</summary>
public static class SampleSufficiencyAssessment
{
    public enum Status { Indeterminate, Insufficient, Sufficient }
    public sealed record Evidence(string Stratum, string PairKey, bool IndependentTruth, bool Match);
    public sealed record Count(string Stratum, int M, int U);
    public sealed record Result(Status State, IReadOnlyList<Count> Counts, IReadOnlyList<string> Reasons);

    public static Result Evaluate(IEnumerable<Evidence> evidence, IReadOnlyCollection<string> strata, int minimumM, int minimumU)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(strata);
        if (minimumM < 1 || minimumU < 1) throw new ArgumentOutOfRangeException(nameof(minimumM));
        if (strata.Count == 0 || strata.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one stratum is required.", nameof(strata));

        var requiredStrata = strata.ToHashSet(StringComparer.Ordinal);
        var eligible = evidence.Where(e => requiredStrata.Contains(e.Stratum) && e.IndependentTruth && !string.IsNullOrWhiteSpace(e.Stratum)
                && !string.IsNullOrWhiteSpace(e.PairKey))
            .GroupBy(e => (e.Stratum, e.PairKey))
            .Where(g => g.Select(e => e.Match).Distinct().Count() == 1)
            .Select(g => g.First()).ToArray();
        var counts = strata.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal)
            .Select(s => new Count(s, eligible.Count(e => e.Stratum == s && e.Match),
                eligible.Count(e => e.Stratum == s && !e.Match))).ToArray();
        var reasons = counts.Where(c => c.M < minimumM || c.U < minimumU)
            .Select(c => $"Stratum {c.Stratum}: m={c.M}, u={c.U}; minimum m={minimumM}, u={minimumU}.")
            .ToArray();
        return new Result(eligible.Length == 0 ? Status.Indeterminate :
            reasons.Length == 0 ? Status.Sufficient : Status.Insufficient, counts, reasons);
    }
}
