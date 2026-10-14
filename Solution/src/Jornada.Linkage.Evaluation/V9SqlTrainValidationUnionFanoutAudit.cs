namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Independently recomputes endpoint degrees for all 56 physical SQL candidate
/// unions, including every eligible zero-hit observation in TRAIN/VALIDATION.
/// Tests are excluded by contract. No rules, coefficients or gates are changed.
/// </summary>
public static class V9SqlTrainValidationUnionFanoutAudit
{
    public const string Version = "V9_SQL_TRAIN_VALIDATION_56_UNIONS_ZERO_INCLUSIVE_FANOUT_V1";
    public const string Status =
        "DESCRIPTIVE_ONLY_NO_TEST_NO_FANOUT_BUDGET_OR_OPERATIONAL_PROMOTION";

    public sealed record Observation(int Id, string Partition, string Gestor, bool Eligible);
    public sealed record Degree(int DegreeValue, long ObservationCount);

    public sealed record Split(
        string Signature, string Partition,
        int EligibleObservations, int ObservationsWithCandidates,
        int ObservationsWithoutCandidates,
        long M, long U, long TotalEndpointIncidences,
        long FalseEndpointIncidences,
        int P50Candidates, int P95Candidates, int P99Candidates, int MaxCandidates,
        int P95FalseCandidates, int P99FalseCandidates, int MaxFalseCandidates,
        IReadOnlyList<Degree> CandidateDegreeHistogram,
        IReadOnlyList<Degree> FalseDegreeHistogram);

    public sealed record Report(
        string Version, string Status, string PartitionScope,
        int EvaluatedUnions, int EligibleTrainObservations,
        int EligibleValidationObservations,
        bool MayPromoteBlockingPolicy,
        IReadOnlyList<Split> Splits);

    public static Report Analyze(
        IReadOnlyList<Observation> observations,
        IReadOnlyList<V9SqlTrainValidationUnionSearch.Pair> baseline,
        IReadOnlyList<V9SqlTrainValidationUnionSearch.Extension> extensions,
        V9SqlTrainValidationUnionSearch.Report search, int pairCap)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(search);
        if (search.Version != V9SqlTrainValidationUnionSearch.Version
            || search.PartitionScope != "TRAIN_AND_VALIDATION_ONLY_TEST_FORBIDDEN"
            || search.MayPromoteBlockingPolicy || search.Combinations.Count != 56
            || extensions.Count != 6 || pairCap <= 0)
            throw new InvalidDataException("Busca SQL insuficiente para auditar fan-out das uniões.");

        var allIds = new HashSet<int>();
        var eligible = new Dictionary<int, Observation>();
        var idsByPart = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var row in observations)
        {
            ArgumentNullException.ThrowIfNull(row);
            if (row.Id < 0 || row.Partition is not ("TRAIN" or "VALIDATION")
                || string.IsNullOrWhiteSpace(row.Gestor) || !allIds.Add(row.Id))
                throw new InvalidDataException("Observação da união fora de TRAIN/VALIDATION.");
            if (row.Eligible)
                eligible.Add(row.Id, row);
        }
        foreach (var part in new[] { "TRAIN", "VALIDATION" })
        {
            var ids = eligible.Values.Where(x => x.Partition == part)
                .Select(x => x.Id).OrderBy(x => x).ToArray();
            if (ids.Length == 0)
                throw new InvalidDataException("Partição sem observações elegíveis.");
            idsByPart.Add(part, ids);
        }

        var baselineEdges = new Dictionary<(int, int), V9SqlTrainValidationUnionSearch.Pair>();
        void Guard(V9SqlTrainValidationUnionSearch.Pair pair)
        {
            ArgumentNullException.ThrowIfNull(pair);
            if (pair.A < 0 || pair.B <= pair.A
                || pair.Partition is not ("TRAIN" or "VALIDATION")
                || !eligible.TryGetValue(pair.A, out var a)
                || !eligible.TryGetValue(pair.B, out var b)
                || a.Partition != pair.Partition || b.Partition != pair.Partition
                || a.Gestor == b.Gestor)
                throw new InvalidDataException(
                    "Aresta SQL fora do frame elegível, entre gestores e na mesma partição.");
        }
        foreach (var pair in baseline)
        {
            Guard(pair);
            if (!baselineEdges.TryAdd((pair.A, pair.B), pair))
                throw new InvalidDataException("Par físico baseline duplicado.");
        }
        if (baselineEdges.Count > pairCap)
            throw new InvalidDataException("Baseline SQL ultrapassa o cap de pares.");

        var byPass = new Dictionary<string,
            Dictionary<(int, int), V9SqlTrainValidationUnionSearch.Pair>>(StringComparer.Ordinal);
        foreach (var pass in extensions)
        {
            ArgumentNullException.ThrowIfNull(pass);
            var set = new Dictionary<(int, int), V9SqlTrainValidationUnionSearch.Pair>();
            foreach (var pair in pass.ExtraPairs)
            {
                Guard(pair);
                if (baselineEdges.ContainsKey((pair.A, pair.B))
                    || !set.TryAdd((pair.A, pair.B), pair))
                    throw new InvalidDataException(
                        "Passe adicional contém aresta baseline ou repetida.");
            }
            if (string.IsNullOrWhiteSpace(pass.PassId)
                || !byPass.TryAdd(pass.PassId, set))
                throw new InvalidDataException("Passe adicional não possui ID único.");
        }

        var splits = new List<Split>(112);
        foreach (var combination in search.Combinations)
        {
            if (combination.PassIds.Count is < 1 or > 4
                || combination.Signature != string.Join("||", combination.PassIds)
                || combination.PassIds.Distinct(StringComparer.Ordinal).Count()
                    != combination.PassIds.Count)
                throw new InvalidDataException("Assinatura da união não canônica.");
            var edges = new Dictionary<(int, int), V9SqlTrainValidationUnionSearch.Pair>(
                baselineEdges);
            foreach (var passId in combination.PassIds)
            {
                if (!byPass.TryGetValue(passId, out var extra))
                    throw new InvalidDataException("Passe desconhecido na união SQL.");
                foreach (var (key, pair) in extra)
                {
                    if (edges.TryGetValue(key, out var previous) && previous != pair)
                        throw new InvalidDataException(
                            "Mesmo par SQL com rótulo/partição incompatível entre passes.");
                    edges.TryAdd(key, pair);
                }
            }
            if (edges.Count > pairCap)
                throw new InvalidDataException("União SQL extrapolou o cap físico.");

            var degrees = eligible.Keys.ToDictionary(x => x, _ => (Total: 0, False: 0));
            var counts = new Dictionary<string, (long M, long U)>(StringComparer.Ordinal)
            {
                ["TRAIN"] = (0, 0),
                ["VALIDATION"] = (0, 0)
            };
            foreach (var pair in edges.Values)
            {
                var a = degrees[pair.A];
                var b = degrees[pair.B];
                checked
                {
                    a.Total++; b.Total++;
                    if (!pair.IsReferenceMatch) { a.False++; b.False++; }
                }
                degrees[pair.A] = a;
                degrees[pair.B] = b;
                var count = counts[pair.Partition];
                if (pair.IsReferenceMatch) count.M++; else count.U++;
                counts[pair.Partition] = count;
            }
            if (counts["TRAIN"].M != combination.TrainUnionM
                || counts["TRAIN"].U != combination.TrainUnionU
                || counts["VALIDATION"].M != combination.ValidationUnionM
                || counts["VALIDATION"].U != combination.ValidationUnionU)
                throw new InvalidDataException(
                    "Fan-out de união SQL diverge da busca deduplicada de M/U.");

            foreach (var part in new[] { "TRAIN", "VALIDATION" })
            {
                var ids = idsByPart[part];
                var total = ids.Select(id => degrees[id].Total).OrderBy(x => x).ToArray();
                var falseDegrees = ids.Select(id => degrees[id].False).OrderBy(x => x).ToArray();
                var count = counts[part];
                var incidences = total.Sum(x => (long)x);
                var falseIncidences = falseDegrees.Sum(x => (long)x);
                var zero = total.Count(x => x == 0);
                if (incidences != checked(2L * (count.M + count.U))
                    || falseIncidences != checked(2L * count.U))
                    throw new InvalidDataException("Incidências de pares SQL inconsistentes.");
                splits.Add(new Split(combination.Signature, part, ids.Length,
                    ids.Length - zero, zero, count.M, count.U,
                    incidences, falseIncidences,
                    Rank(total, 50), Rank(total, 95), Rank(total, 99), total[^1],
                    Rank(falseDegrees, 95), Rank(falseDegrees, 99),
                    falseDegrees[^1], Histogram(total), Histogram(falseDegrees)));
            }
        }
        if (splits.Count != 112 || splits.Select(x => (x.Signature, x.Partition))
                .Distinct().Count() != 112)
            throw new InvalidDataException("Fan-out incompleto das 56 uniões em duas partições.");
        return new Report(Version, Status, "TRAIN_AND_VALIDATION_ONLY_TEST_FORBIDDEN",
            search.Combinations.Count, idsByPart["TRAIN"].Length,
            idsByPart["VALIDATION"].Length,
            false, Array.AsReadOnly(splits.ToArray()));
    }

    private static int Rank(int[] ordered, int percentile) =>
        ordered[(int)((ordered.Length * (long)percentile + 99) / 100) - 1];

    private static IReadOnlyList<Degree> Histogram(int[] ordered) =>
        Array.AsReadOnly(ordered.GroupBy(x => x)
            .Select(group => new Degree(group.Key, group.LongCount())).ToArray());
}
