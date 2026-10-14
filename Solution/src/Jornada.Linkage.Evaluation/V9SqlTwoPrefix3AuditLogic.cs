namespace Jornada.Linkage.Evaluation;

public static partial class V9SqlTwoPrefix3Audit
{
    public static Report Analyze(
        IReadOnlyList<Observation> observations,
        IReadOnlyCollection<V9SqlNamePrefix3BirthAudit.Pair> twelve,
        IReadOnlyCollection<V9SqlNamePrefix3BirthAudit.Pair> truthM,
        IReadOnlyList<Pass> passes,
        V9SqlNamePrefix3BirthAudit.Report prior,
        int cap)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(twelve);
        ArgumentNullException.ThrowIfNull(truthM);
        ArgumentNullException.ThrowIfNull(passes);
        ArgumentNullException.ThrowIfNull(prior);
        if(cap<1 || passes.Count!=2
            || !passes.Select(x=>x.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(new[]{MotherDate,PersonMother}))
            throw new InvalidDataException("Dois passes prefixo3 são obrigatórios.");
        var seen=new Dictionary<int,Observation>();
        foreach(var row in observations)
        {
            ArgumentNullException.ThrowIfNull(row);
            if(row.Id<0 || row.Partition is not ("TRAIN" or "VALIDATION")
                || string.IsNullOrWhiteSpace(row.Gestor)
                || (row.Person3 is not null && row.Person3.Length!=3)
                || (row.Mother3 is not null && row.Mother3.Length!=3)
                || row.Eligible && !row.Birth.HasValue
                || !seen.TryAdd(row.Id,row))
                throw new InvalidDataException("Observações SQL inválidas ou TEST.");
        }
        var eligible=seen.Values.Where(x=>x.Eligible).ToDictionary(x=>x.Id);
        void Guard(V9SqlNamePrefix3BirthAudit.Pair x)
        {
            ArgumentNullException.ThrowIfNull(x);
            if(x.A<0 || x.A>=x.B || x.Partition is not ("TRAIN" or "VALIDATION")
                || !eligible.TryGetValue(x.A,out var a)
                || !eligible.TryGetValue(x.B,out var b)
                || a.Partition!=x.Partition || b.Partition!=x.Partition
                || a.Gestor==b.Gestor)
                throw new InvalidDataException("Par SQL fora de TRAIN/VALIDATION.");
        }
        var truth=new HashSet<(int,int)>();
        foreach(var x in truthM)
        {
            Guard(x);
            if(!x.Match || !truth.Add((x.A,x.B)))
                throw new InvalidDataException("M elegível duplicado/incorreto.");
        }
        var baseline=new Dictionary<(int,int),V9SqlNamePrefix3BirthAudit.Pair>();
        foreach(var x in twelve)
        {
            Guard(x);
            if(baseline.TryGetValue((x.A,x.B),out var prev) && prev!=x)
                throw new InvalidDataException("Conflito de rótulo do SQL existente.");
            baseline.TryAdd((x.A,x.B),x);
            if(baseline.Count>cap)throw new InvalidDataException("12 passes excedem cap.");
        }
        var total=new Dictionary<(int,int),V9SqlNamePrefix3BirthAudit.Pair>(baseline);
        foreach(var pass in passes)
        {
            ArgumentNullException.ThrowIfNull(pass);
            var unique=new HashSet<(int,int)>();
            foreach(var x in pass.Edges)
            {
                Guard(x);
                if(!Matches(pass.Name,x,eligible) || !unique.Add((x.A,x.B))
                    || x.Match!=truth.Contains((x.A,x.B)))
                    throw new InvalidDataException("Par/predicado/rótulo do passe incorreto.");
                if(total.TryGetValue((x.A,x.B),out var old) && old!=x)
                    throw new InvalidDataException("Rótulo SQL conflitante.");
                total.TryAdd((x.A,x.B),x);
                if(total.Count>cap)throw new InvalidDataException("14 passes excedem cap.");
            }
        }
        foreach(var (key,p) in total)
            if(p.Match!=truth.Contains(key))
                throw new InvalidDataException("SQL M/U diverge de verdade independente.");
        var stats=new List<PassSplit>(4);
        foreach(var pass in passes)
        foreach(var part in new[]{"TRAIN","VALIDATION"})
        {
            var got=pass.Edges.Where(x=>x.Partition==part).ToArray();
            long matchesTruth=0,notInSql=0;
            foreach(var x in truthM.Where(x=>x.Partition==part))
            {
                if(!Matches(pass.Name,x,eligible))continue;
                matchesTruth++;
                if(!total.ContainsKey((x.A,x.B)))notInSql++;
            }
            if(notInSql!=0)throw new InvalidDataException("SQL perdeu M que passa no novo predicado.");
            stats.Add(new PassSplit(pass.Name,part,
                got.LongCount(x=>x.Match),got.LongCount(x=>!x.Match),
                got.LongCount(x=>x.Match&&!baseline.ContainsKey((x.A,x.B))),
                got.LongCount(x=>!x.Match&&!baseline.ContainsKey((x.A,x.B))),
                matchesTruth,notInSql));
        }
        return new Report(Version,"DIAGNOSTIC_ONLY_TRAIN_VALIDATION_NO_TEST",false,
            Array.AsReadOnly(stats.ToArray()),
            Array.AsReadOnly(BuildSplits(prior,eligible,truthM,baseline,total,cap).ToArray()));
    }

    private static bool Matches(string pass,V9SqlNamePrefix3BirthAudit.Pair x,
        Dictionary<int,Observation> eligible)
    {
        var a=eligible[x.A];var b=eligible[x.B];
        return pass switch
        {
            MotherDate=>a.Mother3 is not null && a.Mother3==b.Mother3
                && a.Birth==b.Birth,
            PersonMother=>a.Person3 is not null && a.Person3==b.Person3
                && a.Mother3 is not null && a.Mother3==b.Mother3,
            _=>false
        };
    }
}
