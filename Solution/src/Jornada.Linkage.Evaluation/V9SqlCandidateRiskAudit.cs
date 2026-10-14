namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Diagnostic-only, independent risk census of the actual 14-pass SQL candidate
/// union. Buckets are unordered pairs of endpoint evidence combinations, NOT
/// joint name frequencies or calibrated FS false-discovery estimates.
/// </summary>
public static class V9SqlCandidateRiskAudit
{
    public const string Version = "V9_SQL_PAIR_COMBINATION_RISK_TRAIN_VALIDATION_V1";
    public const string TwoField = "TWO_FIELD";
    public const string PersonRequired = "PERSON_REQUIRED";

    public sealed record Bucket(
        string Partition, string Policy, string EndpointCombinations,
        long TruthM, long SqlM, long SqlU,
        decimal? RecallEligibleTruthM, decimal? HypotheticalFdrAcceptAll,
        decimal? UPerRecoveredM);

    public sealed record Fanout(
        string Partition, string Policy, long EligibleObservations,
        long AbstainingObservations, long SqlM, long SqlU,
        long ZeroCandidateObservations, int P95CandidateDegree,
        int P99CandidateDegree, int MaxCandidateDegree,
        int P95UDegree, int P99UDegree, int MaxUDegree,
        decimal? HypotheticalFdrAcceptAll);

    public sealed record Report(
        string Version, string Scope, string AcceptanceScenario,
        bool FdrMeasured, bool MayPromotePolicy,
        IReadOnlyList<Bucket> Buckets, IReadOnlyList<Fanout> Fanout);

    public static Report Analyze(
        IReadOnlyList<V9TwoFieldSqlCoverageAudit.Observation> observations,
        IEnumerable<V9SqlNamePrefix3BirthAudit.Pair> physicalCandidates,
        int hardPairCap)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(physicalCandidates);
        if (hardPairCap is < 1 or > 1_000_000)
            throw new InvalidDataException("Cap SQL físico inválido.");

        var all = new Dictionary<int, V9TwoFieldSqlCoverageAudit.Observation>();
        var evidence = new Dictionary<int, V9MinimumEvidenceEligibility.Result>();
        foreach (var row in observations)
        {
            ArgumentNullException.ThrowIfNull(row);
            if (row.Id < 0 || row.Partition is not ("TRAIN" or "VALIDATION")
                || string.IsNullOrWhiteSpace(row.Base)
                || string.IsNullOrWhiteSpace(row.Gestor)
                || !all.TryAdd(row.Id, row))
                throw new InvalidDataException("Observação duplicada ou TEST.");
            evidence[row.Id] = V9MinimumEvidenceEligibility.Evaluate(
                row.Person, row.Mother, row.Birth?.ToDateTime(TimeOnly.MinValue));
        }

        var noCpf = all.Values.Where(x => x.Cpf is null).ToArray();
        static bool Allowed(string policy, string combination) =>
            policy == TwoField
                ? combination is "PM" or "PD" or "MD" or "PMD"
                : combination is "PM" or "PD" or "PMD";
        static string PairCombination(string a, string b) =>
            string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;

        var counts = new Dictionary<(string Part, string Policy, string Combination),
            (long Truth, long M, long U)>();
        foreach (var part in new[] { "TRAIN", "VALIDATION" })
        foreach (var policy in new[] { TwoField, PersonRequired })
        {
            var combinations = policy == TwoField
                ? new[] { "MD", "PD", "PM", "PMD" }
                : new[] { "PD", "PM", "PMD" };
            for (var i = 0; i < combinations.Length; i++)
            for (var j = i; j < combinations.Length; j++)
                counts.Add((part, policy,
                    PairCombination(combinations[i], combinations[j])), (0, 0, 0));
        }

        foreach (var group in noCpf.GroupBy(x => (x.Partition, x.Base)))
        {
            var rows = group.ToArray();
            for (var i = 0; i < rows.Length; i++)
            for (var j = i + 1; j < rows.Length; j++)
            {
                if (rows[i].Gestor == rows[j].Gestor) continue;
                foreach (var policy in new[] { TwoField, PersonRequired })
                {
                    var ca = evidence[rows[i].Id].Combination;
                    var cb = evidence[rows[j].Id].Combination;
                    if (!Allowed(policy, ca) || !Allowed(policy, cb)) continue;
                    var key = (group.Key.Partition, policy, PairCombination(ca, cb));
                    var prior = counts[key];
                    counts[key] = (checked(prior.Truth + 1), prior.M, prior.U);
                }
            }
        }

        var sql = new Dictionary<(int A, int B), V9SqlNamePrefix3BirthAudit.Pair>();
        foreach (var pair in physicalCandidates)
        {
            ArgumentNullException.ThrowIfNull(pair);
            if (pair.A < 0 || pair.A >= pair.B
                || pair.Partition is not ("TRAIN" or "VALIDATION")
                || !all.TryGetValue(pair.A, out var a)
                || !all.TryGetValue(pair.B, out var b)
                || a.Partition != pair.Partition || b.Partition != pair.Partition
                || a.Cpf is not null || b.Cpf is not null
                || a.Gestor == b.Gestor
                || !a.Birth.HasValue || !b.Birth.HasValue
                || a.Base.Equals(b.Base, StringComparison.Ordinal) != pair.Match)
                throw new InvalidDataException("Par SQL fora do frame físico/gabarito.");
            if (sql.TryGetValue((pair.A, pair.B), out var prior))
            {
                if (prior != pair)
                    throw new InvalidDataException("Rótulo conflitante em passes SQL.");
            }
            else
            {
                sql.Add((pair.A, pair.B), pair);
                if (sql.Count > hardPairCap)
                    throw new InvalidDataException("SQL excedeu cap; não truncar.");
            }
        }

        foreach (var pair in sql.Values)
        foreach (var policy in new[] { TwoField, PersonRequired })
        {
            var ca = evidence[pair.A].Combination;
            var cb = evidence[pair.B].Combination;
            if (!Allowed(policy, ca) || !Allowed(policy, cb)) continue;
            var key = (pair.Partition, policy, PairCombination(ca, cb));
            var old = counts[key];
            counts[key] = pair.Match
                ? (old.Truth, checked(old.M + 1), old.U)
                : (old.Truth, old.M, checked(old.U + 1));
        }

        var buckets = new List<Bucket>(counts.Count);
        foreach (var (key, value) in counts.OrderBy(x => x.Key.Part)
                     .ThenBy(x => x.Key.Policy).ThenBy(x => x.Key.Combination))
        {
            if (value.M > value.Truth)
                throw new InvalidDataException("M SQL excede gabarito por combinação.");
            buckets.Add(new Bucket(key.Part, key.Policy, key.Combination,
                value.Truth, value.M, value.U,
                value.Truth == 0 ? null : decimal.Divide(value.M, value.Truth),
                value.M + value.U == 0 ? null :
                    decimal.Divide(value.U, checked(value.M + value.U)),
                value.M == 0 ? null : decimal.Divide(value.U, value.M)));
        }

        var fanout = new List<Fanout>(4);
        foreach (var part in new[] { "TRAIN", "VALIDATION" })
        foreach (var policy in new[] { TwoField, PersonRequired })
        {
            var eligibleIds = noCpf.Where(x => x.Partition == part
                && Allowed(policy, evidence[x.Id].Combination))
                .Select(x => x.Id).ToArray();
            var degree = eligibleIds.ToDictionary(x => x, _ => (All: 0, U: 0));
            long m = 0, u = 0;
            foreach (var pair in sql.Values.Where(x => x.Partition == part))
            {
                if (!degree.ContainsKey(pair.A) || !degree.ContainsKey(pair.B))
                    continue;
                if (pair.Match) m++; else u++;
                foreach (var id in new[] { pair.A, pair.B })
                {
                    var old = degree[id];
                    degree[id] = (checked(old.All + 1),
                                  checked(old.U + (pair.Match ? 0 : 1)));
                }
            }
            var sumM = buckets.Where(x => x.Partition == part && x.Policy == policy)
                .Sum(x => x.SqlM);
            var sumU = buckets.Where(x => x.Partition == part && x.Policy == policy)
                .Sum(x => x.SqlU);
            if (m != sumM || u != sumU
                || degree.Values.Sum(x => (long)x.All) != 2 * (m + u)
                || degree.Values.Sum(x => (long)x.U) != 2 * u)
                throw new InvalidDataException("Fanout e M/U não reconciliam.");
            var allDegrees = degree.Values.Select(x => x.All).Order().ToArray();
            var uDegrees = degree.Values.Select(x => x.U).Order().ToArray();
            static int Percentile(int[] values, int percentile) =>
                values.Length == 0 ? 0 :
                values[(int)((values.LongLength * percentile + 99) / 100) - 1];
            fanout.Add(new Fanout(part, policy, eligibleIds.LongLength,
                noCpf.LongCount(x => x.Partition == part) - eligibleIds.LongLength,
                m, u, allDegrees.LongCount(x => x == 0),
                Percentile(allDegrees, 95), Percentile(allDegrees, 99),
                allDegrees.Length == 0 ? 0 : allDegrees[^1],
                Percentile(uDegrees, 95), Percentile(uDegrees, 99),
                uDegrees.Length == 0 ? 0 : uDegrees[^1],
                m + u == 0 ? null : decimal.Divide(u, m + u)));
        }
        return new Report(Version, "TRAIN_VALIDATION_NO_CPF_NO_TEST",
            "HYPOTHETICAL_ACCEPT_ALL_SQL_CANDIDATES_NOT_FS_FDR",
            false, false, Array.AsReadOnly(buckets.ToArray()),
            Array.AsReadOnly(fanout.ToArray()));
    }
}
