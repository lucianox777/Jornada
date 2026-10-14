namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Two-endpoint base-identity resampling of the OBSERVED SQL U candidate graph.
/// Edges are never sampled independently: a shared base draw changes every
/// incident U edge together. This describes uncertainty within the synthetic
/// generator and frozen SQL frame, not the full non-match universe or FDR.
/// </summary>
public static class V9SqlUNetworkBootstrapAudit
{
    public const string Version = "V9_SQL_TRAIN_VALIDATION_TWO_ENDPOINT_U_NETWORK_BOOTSTRAP_V1";
    public const string Status =
        "EXPLORATORY_SQL_CANDIDATE_U_GRAPH_NOT_POPULATION_CI_OR_MODEL_GATE";
    public const int Replicates = 1000;

    public sealed record Vertex(string Partition, string SyntheticBaseId);
    public sealed record UEdge(string Partition, string LeftBaseId,
        string RightBaseId, bool InBaseline);

    public sealed record Split(
        string Partition, int EligibleBasePeople, int BasePeopleIncidentToU,
        long BaselineU, long ElevenU, long BaselineM, long ElevenM,
        decimal ObservedBaselineUPerRetrievedM,
        decimal ObservedElevenUPerRetrievedM,
        long BaselineUBootstrapP025, long BaselineUBootstrapP975,
        long ElevenUBootstrapP025, long ElevenUBootstrapP975,
        decimal BaselineUPerMBootstrapP025, decimal BaselineUPerMBootstrapP975,
        decimal ElevenUPerMBootstrapP025, decimal ElevenUPerMBootstrapP975,
        int BootstrapReplicates);

    public sealed record Report(
        string Version, string Status, string PartitionScope,
        ulong SourceSeed, string SourceInputFingerprint,
        bool AccountsForSharedUBaseEndpoints,
        bool IsIndependentEdgeConfidenceInterval,
        bool IsProductionPopulationConfidenceInterval,
        bool EstimatesFullNonMatchUniverseOrFdr,
        bool MayPromoteModelOrBlockingPolicy,
        IReadOnlyList<Split> Splits);

    public static Report Analyze(
        IReadOnlyList<Vertex> vertices,
        IReadOnlyList<UEdge> sqlCandidateUEdges,
        IReadOnlyList<V9SqlClusteredMRecallBootstrapAudit.Contribution> mByBase,
        V9SqlElevenPassResidualAudit.Report eleven,
        ulong sourceSeed, string sourceInputFingerprint)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(sqlCandidateUEdges);
        ArgumentNullException.ThrowIfNull(mByBase);
        ArgumentNullException.ThrowIfNull(eleven);
        if (eleven.Version != V9SqlElevenPassResidualAudit.Version
            || eleven.PartitionScope != "TRAIN_AND_VALIDATION_ONLY_TEST_FORBIDDEN"
            || eleven.MayPromoteBlockingPolicy
            || eleven.Splits.Count != 2
            || string.IsNullOrWhiteSpace(sourceInputFingerprint))
            throw new InvalidDataException("Rede U requer o mesmo censo SQL físico congelado.");

        // A base is the sampling unit, including isolates and bases with no
        // eligible M. No person may appear in both TRAIN and VALIDATION.
        var allBases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in vertices)
        {
            ArgumentNullException.ThrowIfNull(v);
            if (v.Partition is not ("TRAIN" or "VALIDATION")
                || string.IsNullOrWhiteSpace(v.SyntheticBaseId)
                || !allBases.Add(v.SyntheticBaseId))
                throw new InvalidDataException("Base U duplicada, inválida ou TEST.");
        }
        var mIndex = new Dictionary<string,V9SqlClusteredMRecallBootstrapAudit.Contribution>(
            StringComparer.Ordinal);
        foreach (var m in mByBase)
        {
            ArgumentNullException.ThrowIfNull(m);
            if (m.Partition is not ("TRAIN" or "VALIDATION")
                || !allBases.Contains(m.SyntheticBaseId)
                || !mIndex.TryAdd(m.SyntheticBaseId,m)
                || m.EligibleM <= 0 || m.BaselineRecoveredM < 0
                || m.ElevenRecoveredM < m.BaselineRecoveredM
                || m.ElevenRecoveredM > m.EligibleM)
                throw new InvalidDataException("Contribuição M sem base ou fora do frame.");
        }
        var byPartition = vertices.GroupBy(x=>x.Partition,StringComparer.Ordinal)
            .ToDictionary(x=>x.Key,x=>x.OrderBy(y=>y.SyntheticBaseId,StringComparer.Ordinal)
                .ToArray(),StringComparer.Ordinal);
        var vertexPartition = vertices.ToDictionary(
            x=>x.SyntheticBaseId,x=>x.Partition,StringComparer.Ordinal);
        foreach (var edge in sqlCandidateUEdges)
        {
            ArgumentNullException.ThrowIfNull(edge);
            if (edge.Partition is not ("TRAIN" or "VALIDATION")
                || string.IsNullOrWhiteSpace(edge.LeftBaseId)
                || string.IsNullOrWhiteSpace(edge.RightBaseId)
                || edge.LeftBaseId==edge.RightBaseId
                || !vertexPartition.TryGetValue(edge.LeftBaseId,out var left)
                || !vertexPartition.TryGetValue(edge.RightBaseId,out var right)
                || left!=edge.Partition || right!=edge.Partition)
                throw new InvalidDataException(
                    "Aresta U precisa de duas bases distintas do mesmo split SQL.");
        }

        var results = new List<Split>(2);
        foreach (var (part,index) in new[] { ("TRAIN",0),("VALIDATION",1) })
        {
            if (!byPartition.TryGetValue(part,out var bases) || bases.Length<2)
                throw new InvalidDataException("Rede U requer duas bases elegíveis por split.");
            var expected = eleven.Splits.Single(x=>x.Partition==part);
            var edges = sqlCandidateUEdges.Where(x=>x.Partition==part).ToArray();
            var uBaseline = edges.LongCount(x=>x.InBaseline);
            var uEleven = edges.LongLength;
            var mBaseline = mByBase.Where(x=>x.Partition==part)
                .Sum(x=>x.BaselineRecoveredM);
            var mEleven = mByBase.Where(x=>x.Partition==part)
                .Sum(x=>x.ElevenRecoveredM);
            if (uBaseline!=expected.BaselineU || uEleven!=expected.ElevenUnionU
                || mBaseline!=expected.BaselineM || mEleven!=expected.ElevenUnionM
                || mBaseline<=0 || mEleven<=0 || uBaseline<=0)
                throw new InvalidDataException(
                    "Arestas U/M não reconciliam exatamente com o SQL de onze passes.");

            var positions = bases.Select((x,i)=>(x.SyntheticBaseId,i))
                .ToDictionary(x=>x.SyntheticBaseId,x=>x.i,StringComparer.Ordinal);
            var uEdges = edges.Select(x=>
                (A:positions[x.LeftBaseId], B:positions[x.RightBaseId],x.InBaseline))
                .ToArray();
            var baseM = new long[bases.Length];
            var elevenM = new long[bases.Length];
            for (var i=0;i<bases.Length;i++)
                if (mIndex.TryGetValue(bases[i].SyntheticBaseId,out var contribution))
                {
                    baseM[i]=contribution.BaselineRecoveredM;
                    elevenM[i]=contribution.ElevenRecoveredM;
                }
            var incident = new HashSet<int>();
            foreach (var e in uEdges){incident.Add(e.A);incident.Add(e.B);}

            var rng = new Random(unchecked((int)(sourceSeed^(sourceSeed>>32))
                ^ (index==0? 2718281: 3141592)));
            var weights = new int[bases.Length];
            var baselineUValues = new long[Replicates];
            var elevenUValues = new long[Replicates];
            var baselineRatios = new decimal[Replicates];
            var elevenRatios = new decimal[Replicates];
            for (var rep=0;rep<Replicates;rep++)
            {
                Array.Clear(weights);
                for (var draw=0;draw<bases.Length;draw++)
                    weights[rng.Next(bases.Length)]++;
                long bM=0,eM=0,bU=0,eU=0;
                checked
                {
                    for (var i=0;i<bases.Length;i++)
                    {
                        // M has both observations on ONE base: one multiplier.
                        bM+=(long)weights[i]*baseM[i];
                        eM+=(long)weights[i]*elevenM[i];
                    }
                    foreach (var edge in uEdges)
                    {
                        // U joins TWO distinct bases: both endpoint weights.
                        // Shared endpoints thus induce dependence across edges.
                        var multiplicity=(long)weights[edge.A]*weights[edge.B];
                        eU+=multiplicity;
                        if(edge.InBaseline)bU+=multiplicity;
                    }
                }
                if (bM<=0 || eM<=0 || bM>eM || bU>eU)
                    throw new InvalidDataException(
                        "Reamostragem U/M sem suporte para razão; não imputar zero.");
                baselineUValues[rep]=bU;
                elevenUValues[rep]=eU;
                baselineRatios[rep]=decimal.Divide(bU,bM);
                elevenRatios[rep]=decimal.Divide(eU,eM);
            }
            Array.Sort(baselineUValues);Array.Sort(elevenUValues);
            Array.Sort(baselineRatios);Array.Sort(elevenRatios);
            results.Add(new Split(part,bases.Length,incident.Count,
                uBaseline,uEleven,mBaseline,mEleven,
                decimal.Divide(uBaseline,mBaseline),
                decimal.Divide(uEleven,mEleven),
                baselineUValues[24],baselineUValues[974],
                elevenUValues[24],elevenUValues[974],
                baselineRatios[24],baselineRatios[974],
                elevenRatios[24],elevenRatios[974],Replicates));
        }
        return new Report(Version,Status,"TRAIN_AND_VALIDATION_ONLY_TEST_FORBIDDEN",
            sourceSeed,sourceInputFingerprint,true,false,false,false,false,
            Array.AsReadOnly(results.ToArray()));
    }
}
