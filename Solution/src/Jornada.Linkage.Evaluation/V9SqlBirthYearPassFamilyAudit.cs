namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Four fixed, previously unavailable SQL predicate families that tolerate
/// birth-day/month error or changed birth date. All candidates come from actual
/// SQL probes; this auditor does not generate/propose pairs or select a policy.
/// TRAIN/VALIDATION only: TEST is not accepted as input.
/// </summary>
public static class V9SqlBirthYearPassFamilyAudit
{
    public const string Version = "V9_SQL_NEW_BIRTH_YEAR_FAMILY_TRAIN_VALIDATION_V1";
    public const string Status = "EXPLORATORY_SQL_NO_TEST_NO_SELECTION_NO_OPERATIONAL_GATE";
    public static readonly IReadOnlyList<string> RegisteredPasses = Array.AsReadOnly(
        new[] {
            "PERSON_EXACT_PLUS_BIRTH_YEAR",
            "PERSON_PHONETIC_PLUS_BIRTH_YEAR",
            "MOTHER_EXACT_PLUS_BIRTH_YEAR",
            "PERSON_AND_MOTHER_PHONETIC_ANY_BIRTH"
        });

    public sealed record Observation(int Id, string Partition, string Gestor, bool Eligible);
    public sealed record Pair(int A, int B, string Partition, bool Match);
    public sealed record Pass(string PassId, IReadOnlyList<Pair> ExtraPairs);
    public sealed record DegreeCount(int Degree, long Observations);

    public sealed record Split(
        string PassId, string Partition, long EligibleTrueM,
        long BaselineM, long BaselineU, long ExtraM, long ExtraU,
        long UnionM, long UnionU, decimal RecallAgainstAllEligibleM,
        int EligibleObservations, int ObservationsWithoutCandidates,
        int P95TotalCandidates, int P99TotalCandidates, int MaxTotalCandidates,
        int P95FalseCandidates, int P99FalseCandidates, int MaxFalseCandidates,
        IReadOnlyList<DegreeCount> TotalDegreeHistogram,
        IReadOnlyList<DegreeCount> FalseDegreeHistogram);

    public sealed record Report(
        string Version, string Status, string Frame,
        int PassCount, int PerPassSplitCount, bool CanSelectOrPromotePolicy,
        IReadOnlyList<Split> Splits, IReadOnlyList<Split> AllFourUnionCeiling);

    public static Report Analyze(
        IReadOnlyList<Observation> observations,
        IReadOnlyList<Pair> baselinePairs,
        IReadOnlyList<Pass> proposed,
        long trainEligibleM, long validationEligibleM,
        int pairCap)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(baselinePairs);
        ArgumentNullException.ThrowIfNull(proposed);
        if (trainEligibleM <= 0 || validationEligibleM <= 0 || pairCap <= 0
            || proposed.Count != RegisteredPasses.Count
            || !proposed.Select(x => x.PassId).OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(RegisteredPasses.OrderBy(x => x, StringComparer.Ordinal)))
            throw new InvalidDataException("Famílias ou denominadores SQL experimentais inválidos.");

        var ids = new HashSet<int>();
        var eligible = new Dictionary<int, Observation>();
        foreach (var row in observations)
        {
            ArgumentNullException.ThrowIfNull(row);
            if (row.Id < 0 || row.Partition is not ("TRAIN" or "VALIDATION")
                || string.IsNullOrWhiteSpace(row.Gestor) || !ids.Add(row.Id))
                throw new InvalidDataException("Observação não canônica ou partição TEST vedada.");
            if (row.Eligible)
                eligible.Add(row.Id, row);
        }
        var byPartition = new[] { "TRAIN", "VALIDATION" }.ToDictionary(
            part => part, part => eligible.Values.Where(x => x.Partition == part)
                .Select(x => x.Id).OrderBy(x => x).ToArray(), StringComparer.Ordinal);
        if (byPartition.Values.Any(x => x.Length == 0))
            throw new InvalidDataException("Sem observações elegíveis em TRAIN/VALIDATION.");

        void Check(Pair pair)
        {
            ArgumentNullException.ThrowIfNull(pair);
            if (pair.A < 0 || pair.B <= pair.A
                || pair.Partition is not ("TRAIN" or "VALIDATION")
                || !eligible.TryGetValue(pair.A, out var a)
                || !eligible.TryGetValue(pair.B, out var b)
                || a.Partition != pair.Partition || b.Partition != pair.Partition
                || a.Gestor == b.Gestor)
                throw new InvalidDataException("Par SQL fora do mesmo frame elegível inter-Gestor.");
        }
        var baseline = new Dictionary<(int, int), Pair>();
        foreach (var pair in baselinePairs)
        {
            Check(pair);
            if (!baseline.TryAdd((pair.A, pair.B), pair))
                throw new InvalidDataException("Par repetido na linha-base.");
        }
        if (baseline.Count == 0 || baseline.Count > pairCap)
            throw new InvalidDataException("Linha-base SQL vazia ou acima do cap.");

        var extensions = new SortedDictionary<string, Dictionary<(int, int), Pair>>(
            StringComparer.Ordinal);
        foreach (var extension in proposed)
        {
            ArgumentNullException.ThrowIfNull(extension);
            ArgumentNullException.ThrowIfNull(extension.ExtraPairs);
            var observed = new Dictionary<(int, int), Pair>();
            foreach (var pair in extension.ExtraPairs)
            {
                Check(pair);
                if (baseline.ContainsKey((pair.A, pair.B))
                    || !observed.TryAdd((pair.A, pair.B), pair))
                    throw new InvalidDataException("Passe experimental duplica o baseline ou a si próprio.");
            }
            if (!extensions.TryAdd(extension.PassId, observed))
                throw new InvalidDataException("Passes experimentais com IDs repetidos.");
        }
        var all = new Dictionary<(int, int), Pair>(baseline);
        foreach (var extension in extensions.Values)
        foreach (var (key, value) in extension)
        {
            if (all.TryGetValue(key, out var prior) && prior != value)
                throw new InvalidDataException("Passes discordam sobre rótulo M/U ou partição.");
            all.TryAdd(key, value);
        }
        if (all.Count > pairCap)
            throw new InvalidDataException(
                "União completa das novas famílias ultrapassou cap de pares; não truncar.");

        var summaries = new List<Split>(8);
        foreach (var (name, added) in extensions)
        {
            var union = new Dictionary<(int, int), Pair>(baseline);
            foreach (var (key, pair) in added)
                union.Add(key, pair);
            summaries.AddRange(Splits(name, union, baseline, byPartition,
                trainEligibleM, validationEligibleM, pairCap));
        }
        var ceiling = Splits("ALL_FOUR_ONLY_NEW_BIRTH_YEAR_FAMILIES_CEILING",
            all, baseline, byPartition, trainEligibleM, validationEligibleM, pairCap);
        if (summaries.Count != 8 || ceiling.Length != 2
            || summaries.Any(x => x.UnionM > ceiling.Single(c => c.Partition == x.Partition).UnionM
                || x.UnionU > ceiling.Single(c => c.Partition == x.Partition).UnionU))
            throw new InvalidDataException("Limite conjunto inconsistente com passes individuais.");

        return new Report(Version, Status, "TRAIN_AND_VALIDATION_ONLY_TEST_FORBIDDEN",
            RegisteredPasses.Count, summaries.Count, false,
            Array.AsReadOnly(summaries.ToArray()), ceiling);
    }

    private static Split[] Splits(
        string name, IReadOnlyDictionary<(int, int), Pair> union,
        IReadOnlyDictionary<(int, int), Pair> baseline,
        Dictionary<string, int[]> byPartition,
        long eligibleTrainM, long eligibleValidationM, int pairCap)
    {
        if (union.Count > pairCap)
            throw new InvalidDataException("Passe experimental excedeu cap de candidatos.");
        var byId = byPartition.Values.SelectMany(x => x)
            .ToDictionary(x => x, _ => (Total: 0, False: 0));
        var counts = new Dictionary<string, (long M, long U)>(StringComparer.Ordinal)
        {
            ["TRAIN"] = (0, 0), ["VALIDATION"] = (0, 0)
        };
        var baselineCounts = new Dictionary<string, (long M, long U)>(StringComparer.Ordinal)
        {
            ["TRAIN"] = (0, 0), ["VALIDATION"] = (0, 0)
        };
        foreach (var pair in baseline.Values)
        {
            var (m,u) = baselineCounts[pair.Partition];
            if (pair.Match) m++; else u++;
            baselineCounts[pair.Partition] = (m,u);
        }
        foreach (var pair in union.Values)
        {
            var a = byId[pair.A];
            var b = byId[pair.B];
            checked
            {
                a.Total++; b.Total++;
                if (!pair.Match) { a.False++; b.False++; }
            }
            byId[pair.A] = a;
            byId[pair.B] = b;
            var (m,u) = counts[pair.Partition];
            if (pair.Match) m++; else u++;
            counts[pair.Partition] = (m,u);
        }
        var rows = new List<Split>(2);
        foreach (var part in new[] { "TRAIN", "VALIDATION" })
        {
            var ids = byPartition[part];
            var total = ids.Select(id => byId[id].Total).OrderBy(x => x).ToArray();
            var falseDegrees = ids.Select(id => byId[id].False).OrderBy(x => x).ToArray();
            var (m,u) = counts[part];
            var (baseM,baseU) = baselineCounts[part];
            var denominator = part == "TRAIN" ? eligibleTrainM : eligibleValidationM;
            if (m < baseM || u < baseU || m > denominator
                || total.Sum(x => (long)x) != 2 * (m+u)
                || falseDegrees.Sum(x => (long)x) != 2 * u
                || total.Any(x => x < 0)
                || falseDegrees.Any(x => x < 0))
                throw new InvalidDataException("Conciliação de M/U, denom. e incidências falhou.");
            rows.Add(new Split(name, part, denominator,
                baseM, baseU, m-baseM, u-baseU, m,u,
                decimal.Divide(m, denominator), ids.Length,
                total.Count(x => x == 0),
                Rank(total,95), Rank(total,99), total[^1],
                Rank(falseDegrees,95), Rank(falseDegrees,99),
                falseDegrees[^1], Hist(total), Hist(falseDegrees)));
        }
        return rows.ToArray();
    }

    private static int Rank(int[] sorted, int percent) =>
        sorted[(int)((sorted.LongLength*percent+99)/100)-1];

    private static IReadOnlyList<DegreeCount> Hist(int[] sorted) =>
        Array.AsReadOnly(sorted.GroupBy(x => x)
            .Select(group => new DegreeCount(group.Key,group.LongCount())).ToArray());
}
