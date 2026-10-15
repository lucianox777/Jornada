namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Diagnostic support observed in the SQL materialized synthetic frame.
/// Counts joint keys directly, never multiplying person/mother marginals.
/// Neither these keys nor candidate fanout certify an operational FS policy.
/// </summary>
public static class V9SqlTwoPrefix3JointSupportAudit
{
    public const string Version = "V9_SQL_PREFIX3_OBSERVED_JOINT_SUPPORT_V1";
    public const string Status = "TRAIN_VALIDATION_SQL_SYNTHETIC_NO_TEST_NO_PROMOTION";
    public sealed record KeySupport(string Partition, string Pass, int EligibleObservations,
        int DistinctBases, int BasesWithKey, int DistinctJointKeys,
        long DistinctBaseKeyOccurrences, int MaxBasesPerJointKey);
    public sealed record PassFanout(string Partition, string Pass, long M, long U,
        decimal? RecallVsEligibleM, decimal? HypotheticalFdrIfAllAccepted,
        decimal? UPerM, int ZeroCandidateObservations, int P95Degree,
        int P99Degree, int MaxDegree, int P95UDegree, int P99UDegree, int MaxUDegree);
    public sealed record Report(string Version, string Status, bool MayPromotePolicy,
        bool IsObservedPopulationJointDistribution, IReadOnlyList<KeySupport> Keys,
        IReadOnlyList<PassFanout> Passes);

    private readonly record struct JointKey(string First, string Second);

    public static Report Analyze(
        IReadOnlyList<V9SqlTwoPrefix3Probe.Observation> observations,
        IReadOnlyList<V9SqlTwoPrefix3Audit.Pass> passes,
        V9SqlTwoPrefix3Audit.Report physical, int cap)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(passes);
        ArgumentNullException.ThrowIfNull(physical);
        if (cap is < 1 or > 1_000_000 || passes.Count != 2
            || passes.Select(x => x.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { V9SqlTwoPrefix3Audit.MotherDate,
                    V9SqlTwoPrefix3Audit.PersonMother }) == false
            || physical.Splits.Count != 2 || physical.Passes.Count != 4)
            throw new InvalidDataException("Incomplete physical prefix3 audit.");

        var byId = new Dictionary<int,V9SqlTwoPrefix3Probe.Observation>();
        foreach (var row in observations)
        {
            ArgumentNullException.ThrowIfNull(row);
            if (row.Id < 0 || row.Partition is not ("TRAIN" or "VALIDATION")
                || string.IsNullOrWhiteSpace(row.Base)
                || string.IsNullOrWhiteSpace(row.Gestor)
                || !byId.TryAdd(row.Id,row))
                throw new InvalidDataException("Invalid/duplicate SQL observation or TEST leakage.");
        }

        var eligible = byId.Values.Where(x => x.Cpf is null && x.Birth.HasValue)
            .ToDictionary(x => x.Id);
        foreach (var pass in passes)
            if (pass.Edges.Any(x => x.Partition is not ("TRAIN" or "VALIDATION")))
                throw new InvalidDataException("TEST edge is forbidden in joint support audit.");
        var keyRows = new List<KeySupport>(4);
        var fanoutRows = new List<PassFanout>(4);
        foreach (var partition in new[] { "TRAIN", "VALIDATION" })
        {
            var rows = eligible.Values.Where(x => x.Partition == partition).ToArray();
            var split = physical.Splits.Single(x => x.Partition == partition);
            if (rows.Length == 0 || rows.Length != split.EligibleObservations
                || split.EligibleM < 1)
                throw new InvalidDataException("Physical eligibility denominator mismatch.");
            var distinctBases = rows.Select(x => x.Base).Distinct(StringComparer.Ordinal).Count();
            foreach (var pass in passes)
            {
                var observed = rows.Select(x => (
                    x.Base,
                    Key: pass.Name == V9SqlTwoPrefix3Audit.MotherDate
                        ? x.Mother3 is null ? (JointKey?)null
                            : new JointKey(x.Mother3, x.Birth!.Value.ToString("yyyy-MM-dd"))
                        : x.Person3 is null || x.Mother3 is null ? (JointKey?)null
                            : new JointKey(x.Person3, x.Mother3)))
                    .Where(x => x.Key.HasValue)
                    .Select(x => (x.Base, Key: x.Key!.Value))
                    .Distinct().ToArray();
                var grouped = observed.GroupBy(x => x.Key)
                    .Select(g => g.Count()).ToArray();
                keyRows.Add(new KeySupport(partition,pass.Name,rows.Length,
                    distinctBases,observed.Select(x => x.Base)
                        .Distinct(StringComparer.Ordinal).Count(),
                    grouped.Length,observed.LongLength,
                    grouped.Length == 0 ? 0 : grouped.Max()));

                var degree = rows.ToDictionary(x => x.Id,x => (All:0,U:0));
                long m=0,u=0;
                var seen = new HashSet<(int,int)>();
                foreach (var edge in pass.Edges.Where(x => x.Partition == partition))
                {
                    if (edge.A >= edge.B || !seen.Add((edge.A,edge.B))
                        || !eligible.TryGetValue(edge.A,out var a)
                        || !eligible.TryGetValue(edge.B,out var b)
                        || a.Partition != partition || b.Partition != partition
                        || a.Gestor == b.Gestor || !Matches(pass.Name,a,b))
                        throw new InvalidDataException("SQL pass edge violates observed predicate.");
                    if (edge.Match) m++; else u++;
                    foreach (var id in new[] { edge.A,edge.B })
                    {
                        var prev = degree[id];
                        degree[id] = (checked(prev.All+1),
                            checked(prev.U+(edge.Match?0:1)));
                    }
                    if (m+u>cap)
                        throw new InvalidDataException("SQL pass cap exceeded.");
                }
                var expected = physical.Passes.Single(x =>
                    x.Name == pass.Name && x.Partition == partition);
                if (m != expected.M || u != expected.U || m > split.EligibleM)
                    throw new InvalidDataException("M/U does not reconcile with physical SQL.");
                var all = degree.Values.Select(x => x.All).Order().ToArray();
                var wrong = degree.Values.Select(x => x.U).Order().ToArray();
                static int Percentile(int[] values,int p)
                    => values[(int)((values.LongLength*p+99)/100)-1];
                fanoutRows.Add(new PassFanout(partition,pass.Name,m,u,
                    decimal.Divide(m,split.EligibleM),
                    m+u == 0 ? null : decimal.Divide(u,m+u),
                    m == 0 ? null : decimal.Divide(u,m),
                    all.Count(x => x == 0),
                    Percentile(all,95),Percentile(all,99),all[^1],
                    Percentile(wrong,95),Percentile(wrong,99),wrong[^1]));
            }
        }
        return new Report(Version,Status,false,false,
            Array.AsReadOnly(keyRows.ToArray()),
            Array.AsReadOnly(fanoutRows.ToArray()));
    }

    private static bool Matches(string pass,
        V9SqlTwoPrefix3Probe.Observation a,
        V9SqlTwoPrefix3Probe.Observation b) => pass switch
    {
        V9SqlTwoPrefix3Audit.MotherDate => a.Mother3 is not null
            && a.Mother3 == b.Mother3 && a.Birth == b.Birth,
        V9SqlTwoPrefix3Audit.PersonMother => a.Person3 is not null
            && a.Person3 == b.Person3 && a.Mother3 is not null
            && a.Mother3 == b.Mother3,
        _ => false
    };
}
