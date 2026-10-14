namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Diagnostic two-endpoint base-network bootstrap for SQL U candidate exposure.
/// Each U edge receives the product of its two sampled base multiplicities.
/// Resampling edges independently would severely understate shared-base
/// dependencies. No parameter u, FDR or operational gate is estimated.
/// </summary>
public static class V9SqlUNetworkBaseBootstrapAudit
{
    public const string Version = "V9_SQL_TRAIN_VALIDATION_U_NETWORK_BASE_BOOTSTRAP_V1";
    public const string Status =
        "DESCRIPTIVE_TWO_ENDPOINT_NETWORK_BOOTSTRAP_NOT_INFERENTIAL_ESS_OR_ACTIVATION";
    public const int Replicates = 512;

    public sealed record Base(string Partition, string SyntheticBaseId);
    public sealed record Edge(string Partition, string LowerBaseId, string HigherBaseId);
    public sealed record Expected(string Partition, long SqlCandidateU);

    public sealed record Split(
        string Partition, int EligibleBasePeople,
        int BasePeopleTouchingAtLeastOneU,
        long SqlCandidateU,
        long TotalBaseEndpointIncidences,
        decimal BootstrapCandidateUP025,
        decimal BootstrapCandidateUP975,
        decimal BootstrapMedianCandidateU,
        int BootstrapReplicates,
        string SamplingUnit,
        string Estimand);

    public sealed record Report(
        string Version, string Status, string PartitionScope,
        ulong SourceSeed, string SourceInputFingerprint,
        bool UsesBothEndpointsOfEveryUEdge,
        bool TreatsCandidatePairsAsIndependent,
        bool IsProductionUncertaintyOrIndependentEss,
        bool MayPromoteModelOrBlockingPolicy,
        IReadOnlyList<Split> Splits);

    public static Report Analyze(
        IReadOnlyList<Base> eligibleBases,
        IReadOnlyList<Edge> falseCandidateEdges,
        IReadOnlyList<Expected> sqlObserved,
        ulong sourceSeed, string sourceInputFingerprint)
    {
        ArgumentNullException.ThrowIfNull(eligibleBases);
        ArgumentNullException.ThrowIfNull(falseCandidateEdges);
        ArgumentNullException.ThrowIfNull(sqlObserved);
        if (string.IsNullOrWhiteSpace(sourceInputFingerprint)
            || sqlObserved.Count != 2
            || sqlObserved.Select(x => x.Partition).Distinct(StringComparer.Ordinal).Count() != 2
            || sqlObserved.Any(x => x.Partition is not ("TRAIN" or "VALIDATION")
                || x.SqlCandidateU <= 0))
            throw new InvalidDataException("Contagens SQL U e fingerprint inválidos.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var bases = new Dictionary<string,List<string>>(StringComparer.Ordinal)
        {
            ["TRAIN"] = [], ["VALIDATION"] = []
        };
        foreach (var b in eligibleBases)
        {
            ArgumentNullException.ThrowIfNull(b);
            if (b.Partition is not ("TRAIN" or "VALIDATION")
                || string.IsNullOrWhiteSpace(b.SyntheticBaseId)
                || !seen.Add(b.SyntheticBaseId))
                throw new InvalidDataException("Identidade-base duplicada ou TEST no universo elegível.");
            bases[b.Partition].Add(b.SyntheticBaseId);
        }

        var edgesByPart = new Dictionary<string,List<(int A,int B)>>(StringComparer.Ordinal)
        {
            ["TRAIN"] = [], ["VALIDATION"] = []
        };
        var splits = new List<Split>(2);
        foreach (var (part,index) in new[] {("TRAIN",0),("VALIDATION",1)})
        {
            var all = bases[part].OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (all.Length < 3)
                throw new InvalidDataException("Bootstrap da rede U requer 3+ bases elegíveis por split.");
            var locations = all.Select((id,i) => (id,i))
                .ToDictionary(x => x.id,x => x.i,StringComparer.Ordinal);
            foreach (var edge in falseCandidateEdges.Where(x => x.Partition == part))
            {
                ArgumentNullException.ThrowIfNull(edge);
                if (!locations.TryGetValue(edge.LowerBaseId,out var a)
                    || !locations.TryGetValue(edge.HigherBaseId,out var b)
                    || a == b)
                    throw new InvalidDataException(
                        "Aresta U deve conectar duas bases DIFERENTES do mesmo split SQL.");
                // SQL observation-pair multiplicity is preserved: several
                // independently listed pairs can connect the same base nodes.
                edgesByPart[part].Add(a < b ? (a,b) : (b,a));
            }
            var network = edgesByPart[part];
            var expected = sqlObserved.Single(x => x.Partition == part).SqlCandidateU;
            if (network.Count != expected)
                throw new InvalidDataException(
                    "Rede U não concilia com censo SQL físico de candidatos falsos.");
            var degree = new long[all.Length];
            foreach (var (a,b) in network)
            {
                degree[a]++;
                degree[b]++;
            }
            var totalIncidences = degree.Sum();
            if (totalIncidences != checked(2*expected))
                throw new InvalidDataException("Incidências U por pessoa-base inconsistentes.");

            var values = new decimal[Replicates];
            var counts = new int[all.Length];
            var random = new Random(unchecked((int)(sourceSeed ^ (sourceSeed >> 32))
                ^ (index == 0 ? 524287 : 786433)));
            for (int rep=0; rep<Replicates; rep++)
            {
                Array.Clear(counts,0,counts.Length);
                // Multinomial (n draws of n distinct base persons, replacement).
                // U edges are weighted by BOTH sampled base multiplicities.
                for (int draw=0; draw<counts.Length; draw++)
                    counts[random.Next(counts.Length)]++;
                long weighted = 0;
                foreach (var (a,b) in network)
                    weighted = checked(weighted +
                        checked((long)counts[a] * counts[b]));
                // E[w_i w_j]=(n-1)/n for distinct i!=j under multinomial.
                // Multiplying by n/(n-1) corrects ONLY that finite-n mean,
                // not finite-population dependence or target representativity.
                values[rep] = decimal.Divide(
                    checked(weighted*counts.Length), counts.Length-1);
            }
            Array.Sort(values);
            splits.Add(new Split(part, all.Length,
                degree.Count(x=>x>0),expected,totalIncidences,
                values[12],values[499],values[Replicates/2],
                Replicates,"SYNTHETIC_BASE_ID_MULTINOMIAL_TWO_ENDPOINTS",
                "SYNTHETIC_SQL_U_CANDIDATE_EXPOSURE_NOT_FDR_NOT_U_PROBABILITY"));
        }

        if (falseCandidateEdges.Any(x=>x is null
            || x.Partition is not ("TRAIN" or "VALIDATION")))
            throw new InvalidDataException("TEST e arestas U fora do frame são vedados.");
        return new Report(Version,Status,
            "TRAIN_AND_VALIDATION_ONLY_TEST_FORBIDDEN",
            sourceSeed,sourceInputFingerprint,true,false,false,false,
            Array.AsReadOnly(splits.ToArray()));
    }
}
