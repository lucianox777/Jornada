namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Read-only candidate-budget assessment; missing attributes do not invalidate a source fact.</summary>
public static class BlockingSelectivityDiagnostic
{
    public enum Status { NoEvidence, WithinBudget, NeedsUnionMeasurement }
    public sealed record Pass(string Name, long CandidateCount);
    public sealed record Result(Status State, long CandidateCount, long Budget, IReadOnlyList<string> Reasons);

    public static Result Assess(IEnumerable<Pass> passes, long maximumCandidateCount)
    {
        ArgumentNullException.ThrowIfNull(passes);
        if (maximumCandidateCount < 1) throw new ArgumentOutOfRangeException(nameof(maximumCandidateCount));
        var values = passes.ToArray();
        if (values.Any(p => string.IsNullOrWhiteSpace(p.Name) || p.CandidateCount < 0))
            throw new ArgumentException("Invalid pass diagnostics.", nameof(passes));
        if (values.Length == 0)
            return new Result(Status.NoEvidence, 0, maximumCandidateCount,
                new[] { "No blocking pass measurements available." });
        // The sum is an upper bound on the distinct union when pass overlaps are unknown.
        long sum = 0;
        foreach (var pass in values)
        {
            if (pass.CandidateCount > long.MaxValue - sum)
                return new Result(Status.NeedsUnionMeasurement, long.MaxValue, maximumCandidateCount,
                    new[] { "Candidate-count sum overflowed; abstain until union is measured." });
            sum += pass.CandidateCount;
        }
        return sum > maximumCandidateCount
            ? new Result(Status.NeedsUnionMeasurement, sum, maximumCandidateCount,
                new[] { "Sum of pass counts exceeds budget, but overlap is unknown: measure distinct union before deciding." })
            : new Result(Status.WithinBudget, sum, maximumCandidateCount, Array.Empty<string>());
    }
}
