namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Exact, streaming degree census of the CURRENT physical SQL candidate union.
/// A zero-candidate observation is part of the denominator. Nearest-rank
/// percentiles are descriptive, NOT thresholds or service-level assertions.
/// No individual observation, person, name or edge is ever published.
/// </summary>
public static class V9SqlCandidateFanoutAudit
{
    public const string Version = "V9_SQL_PHYSICAL_CANDIDATE_ENDPOINT_FANOUT_V1";
    public const string Status =
        "DIAGNOSTIC_ONLY_ALL_ELIGIBLE_OBSERVATIONS_INCLUDING_ZERO_NO_SCALE_EXTRAPOLATION";

    public sealed record Observation(
        int Id, string Partition, string Gestor, string BasePersonId, bool Eligible);

    public sealed record DegreeCount(int Degree, long Observations);

    public sealed record Split(
        string Partition,
        long EligibleObservations, long WithCandidates, long WithoutCandidates,
        long CandidateTruePairs, long CandidateFalsePairs,
        long DistinctFalseBasePairs,
        long TotalEndpointIncidences, long FalseEndpointIncidences,
        int P50CandidatesPerObservation, int P95CandidatesPerObservation,
        int P99CandidatesPerObservation, int MaxCandidatesPerObservation,
        int P95FalseCandidatesPerObservation, int P99FalseCandidatesPerObservation,
        int MaxFalseCandidatesPerObservation,
        IReadOnlyList<DegreeCount> CandidateDegreeHistogram,
        IReadOnlyList<DegreeCount> FalseDegreeHistogram);

    public sealed record Report(
        string Version, string Status, string PercentileDefinition,
        IReadOnlyList<Split> Splits);

    private sealed class Degree(Observation observation)
    {
        public Observation Source { get; } = observation;
        public int Total { get; set; }
        public int False { get; set; }
    }

    /// <summary>
    /// A single collector handles a full SQL cursor without keeping all pairs,
    /// except the bounded duplicate-key guard and distinct U base-pair sets.
    /// </summary>
    public sealed class Collector
    {
        private static readonly string[] Partitions = ["TRAIN", "VALIDATION", "TEST"];
        private readonly Dictionary<int, Degree> _eligible;
        private readonly HashSet<(int A, int B)> _seen = [];
        private readonly Dictionary<string, (long M, long U)> _candidates;
        private readonly Dictionary<string, HashSet<(string A, string B)>> _uBasePairs;

        public Collector(IEnumerable<Observation> observations)
        {
            ArgumentNullException.ThrowIfNull(observations);
            _eligible = new Dictionary<int, Degree>();
            var identifiers = new HashSet<int>();
            foreach (var row in observations)
            {
                ArgumentNullException.ThrowIfNull(row);
                if (row.Id < 0
                    || !Partitions.Contains(row.Partition, StringComparer.Ordinal)
                    || string.IsNullOrWhiteSpace(row.Gestor)
                    || string.IsNullOrWhiteSpace(row.BasePersonId)
                    || !identifiers.Add(row.Id))
                    throw new InvalidDataException("Registro inválido ou duplicado no frame SQL.");
                if (row.Eligible)
                    _eligible.Add(row.Id, new Degree(row));
            }
            if (_eligible.Count == 0)
                throw new InvalidDataException("Frame de fan-out sem observações elegíveis.");
            _candidates = Partitions.ToDictionary(
                part => part, _ => (M: 0L, U: 0L), StringComparer.Ordinal);
            _uBasePairs = Partitions.ToDictionary(
                part => part,
                _ => new HashSet<(string A, string B)>(),
                StringComparer.Ordinal);
        }

        public void AddCandidate(int a, int b, string partition)
        {
            if (a < 0 || a >= b || !_seen.Add((a, b))
                || !_eligible.TryGetValue(a, out var left)
                || !_eligible.TryGetValue(b, out var right)
                || left.Source.Partition != partition
                || right.Source.Partition != partition
                || left.Source.Gestor == right.Source.Gestor)
                throw new InvalidDataException("Candidato SQL duplicado ou fora do frame elegível.");

            checked
            {
                left.Total++;
                right.Total++;
            }
            var value = _candidates[partition];
            if (left.Source.BasePersonId == right.Source.BasePersonId)
            {
                value.M++;
            }
            else
            {
                value.U++;
                checked
                {
                    left.False++;
                    right.False++;
                }
                var baseA = left.Source.BasePersonId;
                var baseB = right.Source.BasePersonId;
                _uBasePairs[partition].Add(string.CompareOrdinal(baseA, baseB) < 0
                    ? (baseA, baseB) : (baseB, baseA));
            }
            _candidates[partition] = value;
        }

        public Report Build()
        {
            var splits = new List<Split>(3);
            foreach (var partition in Partitions)
            {
                var degrees = _eligible.Values
                    .Where(x => x.Source.Partition == partition)
                    .ToArray();
                if (degrees.Length == 0)
                    throw new InvalidDataException("Split SQL sem observações elegíveis.");
                var totals = degrees.Select(x => x.Total).OrderBy(x => x).ToArray();
                var falses = degrees.Select(x => x.False).OrderBy(x => x).ToArray();
                var counts = _candidates[partition];
                var incidence = totals.Sum(x => (long)x);
                var falseIncidence = falses.Sum(x => (long)x);
                var nonzero = totals.Count(x => x > 0);
                if (nonzero > degrees.Length
                    || incidence != checked(2L * (counts.M + counts.U))
                    || falseIncidence != checked(2L * counts.U)
                    || _uBasePairs[partition].Count > counts.U)
                    throw new InvalidDataException("Fan-out SQL não conciliou os pares recuperados.");
                splits.Add(new Split(partition, degrees.Length,
                    nonzero, degrees.Length - nonzero,
                    counts.M, counts.U, _uBasePairs[partition].Count,
                    incidence, falseIncidence,
                    NearestRank(totals, 50), NearestRank(totals, 95),
                    NearestRank(totals, 99), totals[^1],
                    NearestRank(falses, 95), NearestRank(falses, 99), falses[^1],
                    Histogram(totals), Histogram(falses)));
            }
            if (_seen.Count != splits.Sum(x => x.CandidateTruePairs + x.CandidateFalsePairs))
                throw new InvalidDataException("Fan-out total incompatível com contagem SQL.");
            return new Report(Version, Status,
                "NEAREST_RANK_CEIL_P_N_SORTED_ALL_ELIGIBLE_ENDPOINTS_INCLUDING_ZERO",
                splits);
        }

        private static IReadOnlyList<DegreeCount> Histogram(int[] sorted) =>
            Array.AsReadOnly(sorted.GroupBy(x => x)
                .Select(group => new DegreeCount(group.Key, group.LongCount()))
                .ToArray());

        private static int NearestRank(int[] ordered, int percentile)
        {
            if (ordered.Length == 0 || percentile is < 1 or > 100)
                throw new ArgumentOutOfRangeException(nameof(percentile));
            return ordered[(int)((ordered.Length * (long)percentile + 99) / 100) - 1];
        }
    }
}
