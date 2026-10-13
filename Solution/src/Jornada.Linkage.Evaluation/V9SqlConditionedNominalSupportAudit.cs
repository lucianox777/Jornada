using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// The complete 9x9 joint nominal M/U support from ACTUAL SQL candidates in
/// TRAIN, without pretending repeated observation pairs are independent people.
/// Diagnostic counts only: no smoothing, probabilities, prior, thresholds or TF.
/// </summary>
public static class V9SqlConditionedNominalSupportAudit
{
    public const string Version = "V9_SQL_TRAIN_CONDITIONED_JOINT_NOMINAL_81_SUPPORT_V1";
    public const string Status =
        "TRAIN_SQL_CANDIDATE_COUNTS_ONLY_NO_INDEPENDENT_MU_PRIOR_TF_OR_PRODUCTION";

    public sealed record Candidate(
        int A, int B, string Partition, string BaseA, string BaseB,
        string? NameA, string? NameB,
        string? MotherA, string? MotherB);

    public sealed record Cell(
        CompositeNominalLevelV9 PersonLevel,
        CompositeNominalLevelV9 MotherLevel,
        long MCount, long UCount,
        int DistinctMBasePeople,
        int DistinctUBasePairs,
        int DistinctUBasePeople);

    public sealed record Report(
        string Version, string Status, string Partition,
        long SqlCandidatePairs, long SqlCandidateM, long SqlCandidateU,
        int CellsWithObservedM, int CellsWithObservedU,
        int CellsWithBothMAndU,
        IReadOnlyList<Cell> Cells);

    private sealed class Counts
    {
        public long M, U;
        public readonly HashSet<string> MPeople = new(StringComparer.Ordinal);
        public readonly HashSet<(string A, string B)> UPairs = [];
        public readonly HashSet<string> UPeople = new(StringComparer.Ordinal);
    }

    public static Report Analyze(IEnumerable<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var buckets = new Dictionary<(CompositeNominalLevelV9, CompositeNominalLevelV9), Counts>();
        foreach (var person in Enum.GetValues<CompositeNominalLevelV9>())
        foreach (var mother in Enum.GetValues<CompositeNominalLevelV9>())
            buckets[(person, mother)] = new Counts();
        if (buckets.Count != 81)
            throw new InvalidDataException("Catálogo nominal V9 não contém 81 estados conjuntos.");

        var seen = new HashSet<(int A, int B)>();
        long total = 0, matches = 0, nonmatches = 0;
        foreach (var pair in candidates)
        {
            ArgumentNullException.ThrowIfNull(pair);
            if (pair.Partition != "TRAIN" || pair.A < 0 || pair.B <= pair.A
                || string.IsNullOrWhiteSpace(pair.BaseA)
                || string.IsNullOrWhiteSpace(pair.BaseB)
                || !seen.Add((pair.A, pair.B)))
                throw new InvalidDataException("Candidato SQL duplicado ou fora do TRAIN condicionado.");
            var state = (
                CompositeNominalComparisonV9.Classify(pair.NameA, pair.NameB),
                CompositeNominalComparisonV9.Classify(pair.MotherA, pair.MotherB));
            var bucket = buckets[state];
            total++;
            if (pair.BaseA == pair.BaseB)
            {
                matches++;
                bucket.M++;
                bucket.MPeople.Add(pair.BaseA);
            }
            else
            {
                nonmatches++;
                bucket.U++;
                var ordered = string.CompareOrdinal(pair.BaseA, pair.BaseB) < 0
                    ? (pair.BaseA, pair.BaseB) : (pair.BaseB, pair.BaseA);
                bucket.UPairs.Add(ordered);
                bucket.UPeople.Add(pair.BaseA);
                bucket.UPeople.Add(pair.BaseB);
            }
        }
        if (total == 0 || matches == 0 || nonmatches == 0
            || total != matches + nonmatches)
            throw new InvalidDataException("Amostra TRAIN SQL sem suporte bilateral M/U.");

        var cells = buckets.Select(x => new Cell(
                x.Key.Item1, x.Key.Item2, x.Value.M, x.Value.U,
                x.Value.MPeople.Count, x.Value.UPairs.Count,
                x.Value.UPeople.Count))
            .OrderBy(x => x.PersonLevel)
            .ThenBy(x => x.MotherLevel)
            .ToArray();
        if (cells.Length != 81
            || cells.Sum(x => x.MCount) != matches
            || cells.Sum(x => x.UCount) != nonmatches
            || cells.Any(x => x.DistinctMBasePeople > x.MCount
                || x.DistinctUBasePairs > x.UCount
                || (x.UCount > 0 && x.DistinctUBasePeople < 2)))
            throw new InvalidDataException("Células conjuntas sem reconciliação M/U.");

        return new Report(
            Version, Status, "TRAIN", total, matches, nonmatches,
            cells.Count(x => x.MCount > 0),
            cells.Count(x => x.UCount > 0),
            cells.Count(x => x.MCount > 0 && x.UCount > 0),
            Array.AsReadOnly(cells));
    }
}
