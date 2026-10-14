namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Physical prefix3 + exact birth-date SQL experiment, compared with the
/// deduplicated eleven-pass union on the SAME TRAIN/VALIDATION frame.
/// The independent M truth list is not inferred from returned SQL pairs.
/// No TEST input, operational rule selection, or calibrated FS claim.
/// </summary>
public static class V9SqlNamePrefix3BirthAudit
{
    public const string Version = "V9_SQL_PERSON_PREFIX3_EXACT_BIRTH_TRAIN_VALIDATION_V1";
    public const string PassId = "PERSON_NAME_PREFIX3_AND_EXACT_BIRTH";
    public const string Status = "EXPLORATORY_SQL_NOT_OPERATIONAL_NO_TEST";
    public sealed record Observation(int Id,string Partition,string Gestor,
        bool Eligible,string? Prefix3,DateOnly? Birth);
    public sealed record Pair(int A,int B,string Partition,bool Match);
    public sealed record Split(string Partition,long EligibleM,
        long ElevenM,long ElevenU,long ExtraM,long ExtraU,
        long TwelveM,long TwelveU,decimal TwelveRecall,
        long RequiredAdditionalMFor95Percent,
        long EligibleTrueMWithPrefix3ExactBirth,long Prefix3TruthMAbsentAfterSql,
        int EligibleObservations,int NoCandidateObservations,
        int P95Candidates,int P99Candidates,int MaxCandidates,
        int P95FalseCandidates,int P99FalseCandidates,int MaxFalseCandidates);
    public sealed record Report(string Version,string Status,string PassId,
        string Frame,bool MaySelectOrPromotePolicy,
        IReadOnlyList<Split> Splits);

    public static Report Analyze(
        IReadOnlyList<Observation> observations,
        IReadOnlyCollection<Pair> elevenPassPairs,
        IReadOnlyCollection<Pair> prefixSqlPairs,
        IReadOnlyCollection<Pair> eligibleTruthM,
        IReadOnlyList<V9SqlElevenPassResidualAudit.Split> expectedEleven,
        int hardPairCap)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(elevenPassPairs);
        ArgumentNullException.ThrowIfNull(prefixSqlPairs);
        ArgumentNullException.ThrowIfNull(eligibleTruthM);
        ArgumentNullException.ThrowIfNull(expectedEleven);
        if(hardPairCap<=0 || expectedEleven.Count!=2
            || expectedEleven.Select(x=>x.Partition).OrderBy(x=>x)
                .SequenceEqual(new[]{"TRAIN","VALIDATION"}) is false)
            throw new InvalidDataException("Contrato SQL prefix3 exige dois splits e cap.");
        var all = new Dictionary<int,Observation>();
        foreach(var x in observations)
        {
            ArgumentNullException.ThrowIfNull(x);
            if(x.Id<0 || x.Partition is not ("TRAIN" or "VALIDATION")
                || string.IsNullOrWhiteSpace(x.Gestor)
                || (x.Prefix3 is not null && x.Prefix3.Length!=3)
                || (x.Eligible && !x.Birth.HasValue)
                || !all.TryAdd(x.Id,x))
                throw new InvalidDataException("Observação inválida ou vazamento TEST.");
        }
        var eligible=all.Values.Where(x=>x.Eligible).ToDictionary(x=>x.Id);
        if(eligible.Count==0 || eligible.Count>hardPairCap)
            throw new InvalidDataException("Frame de observações SQL inelegível.");
        void Guard(Pair edge)
        {
            ArgumentNullException.ThrowIfNull(edge);
            if(edge.A<0 || edge.A>=edge.B
                || edge.Partition is not ("TRAIN" or "VALIDATION")
                || !eligible.TryGetValue(edge.A,out var a)
                || !eligible.TryGetValue(edge.B,out var b)
                || a.Partition!=edge.Partition || b.Partition!=edge.Partition
                || a.Gestor==b.Gestor)
                throw new InvalidDataException("Aresta SQL inválida/intergestores/TEST.");
        }
        var truth=new HashSet<(int,int)>();
        foreach(var edge in eligibleTruthM)
        {
            Guard(edge);
            if(!edge.Match || !truth.Add((edge.A,edge.B)))
                throw new InvalidDataException("Gabarito M pré-SQL inválido/duplicado.");
        }
        var eleven=new Dictionary<(int,int),Pair>();
        foreach(var edge in elevenPassPairs)
        {
            Guard(edge);
            var key=(edge.A,edge.B);
            if(eleven.TryGetValue(key,out var prior) && prior!=edge)
                throw new InvalidDataException("Famílias SQL discordam no rótulo.");
            eleven.TryAdd(key,edge);
            if(eleven.Count>hardPairCap)
                throw new InvalidDataException("Censo 11 SQL excede cap.");
        }
        var prefix=new Dictionary<(int,int),Pair>();
        foreach(var edge in prefixSqlPairs)
        {
            Guard(edge);
            var a=eligible[edge.A];var b=eligible[edge.B];
            if(a.Prefix3 is null || a.Prefix3!=b.Prefix3
                || a.Birth!=b.Birth || !prefix.TryAdd((edge.A,edge.B),edge))
                throw new InvalidDataException("Passe SQL prefix3+data contém par fora do predicado.");
            if(prefix.Count>hardPairCap)
                throw new InvalidDataException("Passe SQL prefix3 excede cap.");
        }
        var joined=new Dictionary<(int,int),Pair>(eleven);
        foreach(var (key,value) in prefix)
        {
            if(joined.TryGetValue(key,out var old) && old!=value)
                throw new InvalidDataException("Rótulos SQL inconsistentes entre passes.");
            joined.TryAdd(key,value);
            if(joined.Count>hardPairCap)
                throw new InvalidDataException("União de 12 famílias excede cap.");
        }
        foreach(var (key,value) in joined)
            if(value.Match!=truth.Contains(key))
                throw new InvalidDataException("SQL diverge do gabarito independente.");
        var splits=new List<Split>(2);
        foreach(var part in new[]{"TRAIN","VALIDATION"})
        {
            var e=expectedEleven.Single(x=>x.Partition==part);
            var ids=eligible.Values.Where(x=>x.Partition==part)
                .Select(x=>x.Id).OrderBy(x=>x).ToArray();
            if(ids.Length==0)throw new InvalidDataException("Sem observações elegíveis.");
            var t=eligibleTruthM.LongCount(x=>x.Partition==part);
            var oldM=eleven.Values.LongCount(x=>x.Partition==part&&x.Match);
            var oldU=eleven.Values.LongCount(x=>x.Partition==part&&!x.Match);
            var newM=joined.Values.LongCount(x=>x.Partition==part&&x.Match);
            var newU=joined.Values.LongCount(x=>x.Partition==part&&!x.Match);
            if(t!=e.EligibleM || oldM!=e.ElevenUnionM || oldU!=e.ElevenUnionU
                || newM<oldM || newU<oldU || newM>t)
                throw new InvalidDataException("Passes SQL não conciliam com união de 11.");
            long prefixTruth=0,missing=0;
            foreach(var pair in eligibleTruthM.Where(x=>x.Partition==part))
            {
                var a=eligible[pair.A];var b=eligible[pair.B];
                if(a.Prefix3 is null || a.Prefix3!=b.Prefix3 || a.Birth!=b.Birth)
                    continue;
                prefixTruth++;
                if(!joined.ContainsKey((pair.A,pair.B)))missing++;
            }
            if(missing!=0)throw new InvalidDataException(
                "SQL físico deixou de recuperar M com prefixo3 e data exata.");
            var degree=ids.ToDictionary(x=>x,_=>(Total:0,False:0));
            foreach(var (key,pair) in joined)
            {
                if(pair.Partition!=part)continue;
                var a=degree[key.Item1];var b=degree[key.Item2];
                checked
                {
                    a.Total++;b.Total++;
                    if(!pair.Match){a.False++;b.False++;}
                }
                degree[key.Item1]=a;degree[key.Item2]=b;
            }
            var full=ids.Select(x=>degree[x].Total).Order().ToArray();
            var falseDegree=ids.Select(x=>degree[x].False).Order().ToArray();
            if(full.Sum(x=>(long)x)!=2*(newM+newU)
                || falseDegree.Sum(x=>(long)x)!=2*newU)
                throw new InvalidDataException("Grau zero-inclusivo SQL não reconcilia.");
            static int Rank(int[] xs,int percentile)
                => xs[(int)((xs.LongLength*percentile+99)/100)-1];
            var min95=checked((19*t+19)/20);
            splits.Add(new Split(part,t,oldM,oldU,newM-oldM,newU-oldU,
                newM,newU,decimal.Divide(newM,t),
                Math.Max(0,min95-newM),prefixTruth,missing,ids.Length,
                full.Count(x=>x==0),Rank(full,95),Rank(full,99),full[^1],
                Rank(falseDegree,95),Rank(falseDegree,99),falseDegree[^1]));
        }
        return new Report(Version,Status,PassId,
            "TRAIN_AND_VALIDATION_ONLY_TEST_FORBIDDEN",false,
            Array.AsReadOnly(splits.ToArray()));
    }
}
