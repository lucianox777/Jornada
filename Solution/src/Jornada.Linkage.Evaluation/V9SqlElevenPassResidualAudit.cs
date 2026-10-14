namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Full observed SQL baseline + six registered passes + five new birth-year
/// passes in TRAIN/VALIDATION. This is a candidate-family ceiling only:
/// neither a selected subset nor an operational recall or precision claim.
/// </summary>
public static class V9SqlElevenPassResidualAudit
{
    public const string Version = "V9_SQL_TRAIN_VALIDATION_ELEVEN_PASS_UNION_RESIDUAL_V1";
    public const string Status = "RESIDUAL_DIAGNOSTIC_ONLY_NOT_BLOCKING_POLICY_OR_MODEL";
    public sealed record LossCell(
        string Partition, string NameState, string MotherState, string BirthState,
        long EligibleM, long RecoveredBaselineM, long RecoveredElevenM,
        long ResidualUnretrievedM);

    public sealed record Split(
        string Partition, long EligibleM, long BaselineM, long BaselineU,
        long SixUnionM, long SixUnionU, long FiveUnionM, long FiveUnionU,
        long ElevenUnionM, long ElevenUnionU,
        long RecoveredOnlyBySixM, long RecoveredOnlyBySixU,
        long RecoveredOnlyByFiveM, long RecoveredOnlyByFiveU,
        long AdditionalMNeededFor95Percent, long ResidualUnretrievedM,
        decimal ElevenRecallAgainstEligibleM,
        int EligibleObservations, int ObservationsWithoutCandidates,
        int P50Candidates, int P95Candidates, int P99Candidates,
        int MaxCandidates, int P95FalseCandidates, int P99FalseCandidates,
        int MaxFalseCandidates);

    public sealed record Report(
        string Version, string Status, string PartitionScope,
        int OldRegisteredPassCount, int NewRegisteredPassCount,
        bool Passes95PercentInBothSplits, bool MayPromoteBlockingPolicy,
        IReadOnlyList<Split> Splits, IReadOnlyList<LossCell> ResidualCells)
    {
        public V9SqlClusteredMRecallBootstrapAudit.Report? ClusteredMRecallBootstrap { get; init; }
        public V9SqlUNetworkBaseBootstrapAudit.Report? ClusteredUNetworkBootstrap { get; init; }
        public V9SqlTwoSignalResidualAudit.Report? ThreeAnchorResidualAudit { get; init; }
    }

    public static Report Analyze(
        IReadOnlyList<V9SqlBirthYearPassFamilyAudit.Observation> observations,
        IReadOnlyList<V9SqlTrainValidationUnionSearch.Pair> baseline,
        IReadOnlyList<V9SqlTrainValidationUnionSearch.Extension> oldPasses,
        IReadOnlyList<V9SqlBirthYearPassFamilyAudit.Pass> newPasses,
        IReadOnlyList<V9BlockingTruthLossAudit.Pair> eligibleTruthPairs,
        V9SqlTrainValidationUnionSearch.Report oldSearch,
        V9SqlBirthYearPassFamilyAudit.Report newSearch,
        int hardPairCap,
        IReadOnlyDictionary<int,string>? syntheticBaseByObservation = null,
        ulong sourceSeed = 0, string? sourceInputFingerprint = null,
        IReadOnlyList<V9SqlTwoSignalResidualAudit.Signal>? physicalSignals = null)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(oldPasses);
        ArgumentNullException.ThrowIfNull(newPasses);
        ArgumentNullException.ThrowIfNull(eligibleTruthPairs);
        ArgumentNullException.ThrowIfNull(oldSearch);
        ArgumentNullException.ThrowIfNull(newSearch);
        if (oldSearch.Version != V9SqlTrainValidationUnionSearch.Version
            || newSearch.Version != V9SqlBirthYearPassFamilyAudit.Version
            || oldSearch.PartitionScope != "TRAIN_AND_VALIDATION_ONLY_TEST_FORBIDDEN"
            || newSearch.Frame != "TRAIN_AND_VALIDATION_ONLY_TEST_FORBIDDEN"
            || oldSearch.MayPromoteBlockingPolicy || newSearch.CanSelectOrPromotePolicy
            || hardPairCap <= 0 || oldPasses.Count != 6 || newPasses.Count != 5
            || oldSearch.AllSixRegisteredPassesCeiling is null
            || newSearch.AllFiveUnionCeiling.Count != 2)
            throw new InvalidDataException("Famílias antigas e novas sem suporte SQL físico confiável.");

        var allIds = new HashSet<int>();
        var eligible = new Dictionary<int,V9SqlBirthYearPassFamilyAudit.Observation>();
        foreach (var observation in observations)
        {
            ArgumentNullException.ThrowIfNull(observation);
            if (observation.Id < 0
                || observation.Partition is not ("TRAIN" or "VALIDATION")
                || string.IsNullOrWhiteSpace(observation.Gestor)
                || !allIds.Add(observation.Id))
                throw new InvalidDataException("Observações SQL inválidas/TEST no censo de onze passes.");
            if (observation.Eligible) eligible.Add(observation.Id, observation);
        }
        var idsByPart = new[] { "TRAIN", "VALIDATION" }
            .ToDictionary(p=>p,p=>eligible.Values.Where(x=>x.Partition==p)
                .Select(x=>x.Id).OrderBy(x=>x).ToArray(), StringComparer.Ordinal);
        if (idsByPart.Values.Any(x=>x.Length==0))
            throw new InvalidDataException("Sem observações elegíveis nos dois splits.");

        void Guard(int a, int b, string partition)
        {
            if (a<0 || a>=b || partition is not ("TRAIN" or "VALIDATION")
                || !eligible.TryGetValue(a,out var left)
                || !eligible.TryGetValue(b,out var right)
                || left.Partition!=partition || right.Partition!=partition
                || left.Gestor==right.Gestor)
                throw new InvalidDataException("Aresta SQL fora do quadro TRAIN/VALIDATION elegível.");
        }
        var baseEdges = new Dictionary<(int A,int B),(string Part,bool Match)>();
        foreach (var pair in baseline)
        {
            ArgumentNullException.ThrowIfNull(pair);
            Guard(pair.A,pair.B,pair.Partition);
            if (!baseEdges.TryAdd((pair.A,pair.B),(pair.Partition,pair.IsReferenceMatch)))
                throw new InvalidDataException("Baseline possui aresta duplicada.");
        }
        if (baseEdges.Count>hardPairCap) throw new InvalidDataException("Baseline excedeu cap SQL.");

        var six = new Dictionary<(int A,int B),(string Part,bool Match)>(baseEdges);
        var five = new Dictionary<(int A,int B),(string Part,bool Match)>(baseEdges);
        var eleven = new Dictionary<(int A,int B),(string Part,bool Match)>(baseEdges);

        void Include(Dictionary<(int A,int B),(string Part,bool Match)> destination,
            int a, int b, string part, bool match)
        {
            Guard(a,b,part);
            var key=(a,b);
            var value=(part,match);
            if (baseEdges.ContainsKey(key))
                throw new InvalidDataException("Passe extra repete candidato baseline.");
            if (destination.TryGetValue(key,out var previous) && previous!=value)
                throw new InvalidDataException("Passes discordam sobre a classe ou o split.");
            destination.TryAdd(key,value);
            if (destination.Count>hardPairCap)
                throw new InvalidDataException("União de passes excede cap: proibido truncar.");
        }

        var passIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pass in oldPasses)
        {
            ArgumentNullException.ThrowIfNull(pass);
            if (string.IsNullOrWhiteSpace(pass.PassId)
                || !passIds.Add(pass.PassId))
                throw new InvalidDataException("Passe SQL antigo duplicado.");
            var local = new HashSet<(int,int)>();
            foreach (var pair in pass.ExtraPairs)
            {
                if (!local.Add((pair.A,pair.B)))
                    throw new InvalidDataException("Aresta duplicada dentro do mesmo passe.");
                Include(six,pair.A,pair.B,pair.Partition,pair.IsReferenceMatch);
                Include(eleven,pair.A,pair.B,pair.Partition,pair.IsReferenceMatch);
            }
        }
        foreach (var pass in newPasses)
        {
            ArgumentNullException.ThrowIfNull(pass);
            if (string.IsNullOrWhiteSpace(pass.PassId) || !passIds.Add(pass.PassId))
                throw new InvalidDataException("Passe SQL novo duplicado ou conflita com antigo.");
            var local = new HashSet<(int,int)>();
            foreach (var pair in pass.ExtraPairs)
            {
                if (!local.Add((pair.A,pair.B)))
                    throw new InvalidDataException("Aresta duplicada dentro do passe novo.");
                Include(five,pair.A,pair.B,pair.Partition,pair.Match);
                Include(eleven,pair.A,pair.B,pair.Partition,pair.Match);
            }
        }
        if (passIds.Count!=11)
            throw new InvalidDataException("Censo SQL das onze famílias incompleto.");

        var truth = new Dictionary<(int A,int B),V9BlockingTruthLossAudit.Pair>();
        foreach (var pair in eligibleTruthPairs.Where(x=>x.Partition is "TRAIN" or "VALIDATION"))
        {
            ArgumentNullException.ThrowIfNull(pair);
            Guard(pair.LowerObservationId,pair.HigherObservationId,pair.Partition);
            if (!truth.TryAdd((pair.LowerObservationId,pair.HigherObservationId),pair)
                || (pair.NameMissing && pair.NameExact)
                || (pair.MotherMissing && pair.MotherExact))
                throw new InvalidDataException("Gabarito de M independente inválido/duplicado.");
        }
        if (eligibleTruthPairs.Any(x=>x.Partition is not ("TRAIN" or "VALIDATION" or "TEST")))
            throw new InvalidDataException("Partição do gabarito inválida.");

        void CheckTruth(IReadOnlyDictionary<(int A,int B),(string Part,bool Match)> map)
        {
            foreach (var (pair,label) in map)
                if (label.Match != truth.TryGetValue(pair,out var t)
                    || (label.Match && t!.Partition!=label.Part))
                    throw new InvalidDataException(
                        "Candidatos M/U discordam do gabarito independente pré-SQL.");
        }
        CheckTruth(baseEdges); CheckTruth(six); CheckTruth(five); CheckTruth(eleven);

        static (long M,long U) Count(
            IReadOnlyDictionary<(int,int),(string Part,bool Match)> map,string part)
        {
            long m=0,u=0;
            foreach (var label in map.Values.Where(x=>x.Part==part))
            {
                if (label.Match)m++;else u++;
            }
            return (m,u);
        }
        var residuals = new List<LossCell>(36);
        var summaries = new List<Split>(2);
        foreach (var part in new[] {"TRAIN","VALIDATION"})
        {
            var b=Count(baseEdges,part);
            var s=Count(six,part);
            var f=Count(five,part);
            var both=Count(eleven,part);
            var expected=oldSearch.AllSixRegisteredPassesCeiling;
            var officialSix=part=="TRAIN" ?
                (expected.TrainUnionM,expected.TrainUnionU) :
                (expected.ValidationUnionM,expected.ValidationUnionU);
            var officialFive=newSearch.AllFiveUnionCeiling.Single(x=>x.Partition==part);
            if (s!=officialSix || f!=(officialFive.UnionM,officialFive.UnionU)
                || b!=(officialFive.BaselineM,officialFive.BaselineU)
                || b!=(part=="TRAIN" ? (oldSearch.BaselineTrainM,oldSearch.BaselineTrainU) :
                    (oldSearch.BaselineValidationM,oldSearch.BaselineValidationU)))
                throw new InvalidDataException(
                    "Teto de onze passes diverge dos censos SQL publicados para 6 e 5.");

            var partTruth=truth.Values.Where(x=>x.Partition==part).ToArray();
            var denominator=part=="TRAIN"?oldSearch.TrainEligibleM:oldSearch.ValidationEligibleM;
            if (partTruth.LongLength!=denominator || both.M>denominator)
                throw new InvalidDataException("Denominador de M mudou no teto de onze.");

            foreach (var name in new[] {"EXACT","DIFFERENT","MISSING"})
            foreach (var mother in new[] {"EXACT","DIFFERENT","MISSING"})
            foreach (var birth in new[] {"EXACT","DIFFERENT"})
            {
                var cell=partTruth.Where(x=>
                    State(x.NameMissing,x.NameExact)==name
                    && State(x.MotherMissing,x.MotherExact)==mother
                    && (x.BirthExact ? "EXACT":"DIFFERENT")==birth).ToArray();
                var oldRecovered=cell.LongCount(x=>baseEdges.ContainsKey(
                    (x.LowerObservationId,x.HigherObservationId)));
                var newRecovered=cell.LongCount(x=>eleven.ContainsKey(
                    (x.LowerObservationId,x.HigherObservationId)));
                if (newRecovered<oldRecovered || newRecovered>cell.LongLength)
                    throw new InvalidDataException("Decomposição residual não reconcilia M.");
                residuals.Add(new LossCell(part,name,mother,birth,
                    cell.LongLength,oldRecovered,newRecovered,cell.LongLength-newRecovered));
            }

            var ids=idsByPart[part];
            var degree=ids.ToDictionary(x=>x,_=>(Total:0,False:0));
            foreach (var (pair,label) in eleven)
            {
                if (label.Part!=part)continue;
                var a=degree[pair.A];var z=degree[pair.B];
                checked {
                    a.Total++;z.Total++;
                    if (!label.Match){a.False++;z.False++;}
                }
                degree[pair.A]=a;degree[pair.B]=z;
            }
            var total=ids.Select(x=>degree[x].Total).OrderBy(x=>x).ToArray();
            var falseDegrees=ids.Select(x=>degree[x].False).OrderBy(x=>x).ToArray();
            if (total.Sum(x=>(long)x)!=2*(both.M+both.U)
                || falseDegrees.Sum(x=>(long)x)!=2*both.U
                || residuals.Where(x=>x.Partition==part).Sum(x=>x.EligibleM)!=denominator
                || residuals.Where(x=>x.Partition==part).Sum(x=>x.RecoveredElevenM)!=both.M
                || residuals.Where(x=>x.Partition==part).Sum(x=>x.RecoveredBaselineM)!=b.M)
                throw new InvalidDataException("Grau ou células M não conciliam com SQL.");

            var onlySix=Count(six.Where(x=>!five.ContainsKey(x.Key))
                .ToDictionary(x=>x.Key,x=>x.Value),part);
            var onlyFive=Count(five.Where(x=>!six.ContainsKey(x.Key))
                .ToDictionary(x=>x.Key,x=>x.Value),part);
            if (both.M!=f.M+onlySix.M || both.M!=s.M+onlyFive.M
                || both.U!=f.U+onlySix.U || both.U!=s.U+onlyFive.U)
                throw new InvalidDataException("Inclusão-exclusão entre as famílias falhou.");
            var minimum=checked((19*denominator+19)/20);
            summaries.Add(new Split(part,denominator,b.M,b.U,s.M,s.U,f.M,f.U,both.M,both.U,
                onlySix.M,onlySix.U,onlyFive.M,onlyFive.U,
                Math.Max(0,minimum-both.M),denominator-both.M,
                decimal.Divide(both.M,denominator),ids.Length,
                total.Count(x=>x==0),
                Percentile(total,50),Percentile(total,95),Percentile(total,99),total[^1],
                Percentile(falseDegrees,95),Percentile(falseDegrees,99),falseDegrees[^1]));
        }
        var report = new Report(Version,Status,"TRAIN_AND_VALIDATION_ONLY_TEST_FORBIDDEN",
            oldPasses.Count,newPasses.Count,
            summaries.All(x=>20*x.ElevenUnionM>=19*x.EligibleM),
            false,Array.AsReadOnly(summaries.ToArray()),
            Array.AsReadOnly(residuals.ToArray()));

        if (syntheticBaseByObservation is null && sourceInputFingerprint is null
            && physicalSignals is null)
            return report; // Unit-only invocation; physical probe supplies all three.
        if (physicalSignals is null)
            throw new InvalidDataException("Sinais de âncoras SQL ausentes no censo físico.");
        if (syntheticBaseByObservation is null || string.IsNullOrWhiteSpace(sourceInputFingerprint)
            || syntheticBaseByObservation.Count != observations.Count
            || syntheticBaseByObservation.Keys.Any(x => !allIds.Contains(x)))
            throw new InvalidDataException("Base sintética/fingerprint incompletos no SQL físico.");

        var signalAudit = V9SqlTwoSignalResidualAudit.Analyze(
            physicalSignals,
            truth.Values.Select(x=>new V9SqlTwoSignalResidualAudit.Pair(
                x.LowerObservationId,x.HigherObservationId,x.Partition)).ToArray(),
            eleven.Where(x=>x.Value.Match).Select(x=>x.Key).ToArray());
        foreach (var split in summaries)
        {
            var compatible=signalAudit.Splits.Single(x=>x.Partition==split.Partition);
            if (compatible.EligibleM!=split.EligibleM
                || compatible.RecoveredElevenM!=split.ElevenUnionM
                || compatible.ResidualM!=split.ResidualUnretrievedM)
                throw new InvalidDataException(
                    "Âncoras fonéticas/ano não conciliam com M do SQL onze passes.");
        }

        var byBase = new Dictionary<(string Partition,string Base),
            (long Eligible,long Baseline,long Eleven)>();
        foreach (var pair in truth.Values)
        {
            if (!syntheticBaseByObservation.TryGetValue(pair.LowerObservationId, out var left)
                || !syntheticBaseByObservation.TryGetValue(pair.HigherObservationId, out var right)
                || string.IsNullOrWhiteSpace(left) || left != right)
                throw new InvalidDataException("M verdadeiro não possui uma única identidade-base.");
            var key = (pair.Partition, left);
            var value = byBase.GetValueOrDefault(key);
            var edge = (pair.LowerObservationId,pair.HigherObservationId);
            byBase[key] = (checked(value.Eligible+1),
                checked(value.Baseline+(baseEdges.ContainsKey(edge)?1:0)),
                checked(value.Eleven+(eleven.ContainsKey(edge)?1:0)));
        }
        var contributions = byBase.Select(x => new V9SqlClusteredMRecallBootstrapAudit.Contribution(
            x.Key.Partition,x.Key.Base,x.Value.Eligible,x.Value.Baseline,x.Value.Eleven)).ToArray();

        // A U edge touches TWO distinct base people, potentially with many
        // observation-pair edges between the same nodes. Re-sample the entire
        // base graph (including zero-U eligible bases), never individual U
        // pairs. The synthetic IDs remain in memory and do not enter JSON.
        var eligibleBaseSet = eligible.Values
            .Select(x => new V9SqlUNetworkBaseBootstrapAudit.Base(
                x.Partition,syntheticBaseByObservation[x.Id]))
            .Distinct()
            .ToArray();
        var uNetwork = eleven
            .Where(x => !x.Value.Match)
            .Select(x => new V9SqlUNetworkBaseBootstrapAudit.Edge(
                x.Value.Part,
                syntheticBaseByObservation[x.Key.A],
                syntheticBaseByObservation[x.Key.B]))
            .ToArray();
        var uReport = V9SqlUNetworkBaseBootstrapAudit.Analyze(
            eligibleBaseSet, uNetwork,
            summaries.Select(x => new V9SqlUNetworkBaseBootstrapAudit.Expected(
                x.Partition,x.ElevenUnionU)).ToArray(),
            sourceSeed,sourceInputFingerprint);
        return report with
        {
            ClusteredMRecallBootstrap = V9SqlClusteredMRecallBootstrapAudit.Analyze(
                contributions,report,sourceSeed,sourceInputFingerprint),
            ClusteredUNetworkBootstrap = uReport,
            ThreeAnchorResidualAudit = signalAudit
        };
    }

    private static string State(bool missing,bool exact) =>
        missing?"MISSING":exact?"EXACT":"DIFFERENT";
    private static int Percentile(int[] sorted,int percentile) =>
        sorted[(int)((sorted.LongLength*percentile+99)/100)-1];
}
