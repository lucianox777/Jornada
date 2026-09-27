namespace Jornada.Linkage.Runner;

public sealed record BlockingCandidateClause(string Feature, IReadOnlyList<string> Values);

public sealed record BlockingCandidatePassLookup(
    string PassId,
    IReadOnlyList<BlockingCandidateClause> Clauses);

