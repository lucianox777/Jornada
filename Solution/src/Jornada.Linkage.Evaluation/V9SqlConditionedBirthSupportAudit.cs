using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Full SQL TRAIN candidate census for the existing binary V9 birth-evidence
/// and seven mutually exclusive semantic birth states. Counts observation
/// pairs and distinct underlying base-person groups separately.
/// Diagnostic only: no m/u probabilities, independent ESS, prior or activation.
/// </summary>
public static class V9SqlConditionedBirthSupportAudit
{
    public const string Version = "V9_SQL_TRAIN_BIRTH_SEMANTIC_AND_BINARY_MU_SUPPORT_V1";
    public const string Status = "TRAIN_SQL_OBSERVED_BIRTH_COUNTS_NOT_CALIBRATED_MU_PRIOR_ESS_OR_OPERATIONAL";

    public sealed record Candidate(
        int A, int B, string Partition, string BaseA, string BaseB,
        DateOnly BirthA, DateOnly BirthB);

    public sealed record Cell(
        string SemanticBirthState, long MCount, long UCount,
        int DistinctMBasePeople, int DistinctUBasePairs,
        int DistinctUBasePeople);

    public sealed record Report(
        string Version, string Status, string Partition,
        long SqlCandidatePairs, long SqlCandidateM, long SqlCandidateU,
        long BirthExactM, long BirthExactU,
        long BirthDifferentM, long BirthDifferentU,
        int StatesWithObservedM, int StatesWithObservedU,
        int StatesWithBothMAndU,
        IReadOnlyList<Cell> SemanticCells);

    private sealed class Counts
    {
        public long M;
        public long U;
        public HashSet<string> MPeople { get; } = new(StringComparer.Ordinal);
        public HashSet<(string A, string B)> UPairs { get; } = [];
        public HashSet<string> UPeople { get; } = new(StringComparer.Ordinal);
    }

    public static Report Analyze(IEnumerable<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var states = BirthDateSemanticEvidence.States;
        if (states.Count != 7
            || states.Distinct(StringComparer.Ordinal).Count() != 7
            || !states.Contains(BirthDateSemanticEvidence.Exact, StringComparer.Ordinal))
            throw new InvalidDataException("Contrato de nascimento semântico não possui sete estados distintos.");
        var counts = states.ToDictionary(
            x => x, _ => new Counts(), StringComparer.Ordinal);
        var seen = new HashSet<(int A, int B)>();
        long m = 0;
        long u = 0;
        foreach (var pair in candidates)
        {
            ArgumentNullException.ThrowIfNull(pair);
            if (pair.Partition != "TRAIN"
                || pair.A < 0 || pair.B <= pair.A
                || string.IsNullOrWhiteSpace(pair.BaseA)
                || string.IsNullOrWhiteSpace(pair.BaseB)
                || !seen.Add((pair.A, pair.B)))
                throw new InvalidDataException("Candidato de nascimento SQL duplicado ou fora do TRAIN.");
            var state = BirthDateSemanticEvidence.Classify(pair.BirthA, pair.BirthB);
            var cell = counts[state];
            if (pair.BaseA == pair.BaseB)
            {
                m++;
                cell.M++;
                cell.MPeople.Add(pair.BaseA);
            }
            else
            {
                u++;
                cell.U++;
                var key = string.CompareOrdinal(pair.BaseA, pair.BaseB) < 0
                    ? (pair.BaseA, pair.BaseB) : (pair.BaseB, pair.BaseA);
                cell.UPairs.Add(key);
                cell.UPeople.Add(pair.BaseA);
                cell.UPeople.Add(pair.BaseB);
            }
        }
        if (m <= 0 || u <= 0)
            throw new InvalidDataException("Não existe suporte M/U bilateral em TRAIN para nascimento SQL.");
        var cells = states.Select(x => new Cell(
            x, counts[x].M, counts[x].U,
            counts[x].MPeople.Count, counts[x].UPairs.Count,
            counts[x].UPeople.Count)).ToArray();
        var exact = counts[BirthDateSemanticEvidence.Exact];
        if (cells.Sum(x => x.MCount) != m
            || cells.Sum(x => x.UCount) != u
            || cells.Any(x => x.DistinctMBasePeople > x.MCount
                || x.DistinctUBasePairs > x.UCount
                || x.DistinctUBasePeople > 2L * x.DistinctUBasePairs
                || (x.UCount > 0 && x.DistinctUBasePeople < 2)))
            throw new InvalidDataException("Suporte semântico de nascimento não concilia.");

        return new Report(
            Version, Status, "TRAIN", m + u, m, u,
            exact.M, exact.U, m - exact.M, u - exact.U,
            cells.Count(x => x.MCount > 0),
            cells.Count(x => x.UCount > 0),
            cells.Count(x => x.MCount > 0 && x.UCount > 0),
            Array.AsReadOnly(cells));
    }
}
