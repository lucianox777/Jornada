namespace Jornada.Linkage.Evaluation;

public static partial class V9SqlTwoPrefix3Audit
{
    private static List<Split> BuildSplits(
        V9SqlNamePrefix3BirthAudit.Report previous,
        Dictionary<int,Observation> eligible,
        IReadOnlyCollection<V9SqlNamePrefix3BirthAudit.Pair> truth,
        Dictionary<(int,int),V9SqlNamePrefix3BirthAudit.Pair> old,
        Dictionary<(int,int),V9SqlNamePrefix3BirthAudit.Pair> combined,int cap)
    {
        var result=new List<Split>(2);
        if(previous.Splits.Count!=2)throw new InvalidDataException("Relatório anterior incompleto.");
        foreach(var part in new[]{"TRAIN","VALIDATION"})
        {
            var expected=previous.Splits.Single(x=>x.Partition==part);
            var ids=eligible.Values.Where(x=>x.Partition==part).Select(x=>x.Id).ToArray();
            long t=truth.LongCount(x=>x.Partition==part);
            long a=old.Values.LongCount(x=>x.Partition==part&&x.Match);
            long b=old.Values.LongCount(x=>x.Partition==part&&!x.Match);
            long m=combined.Values.LongCount(x=>x.Partition==part&&x.Match);
            long u=combined.Values.LongCount(x=>x.Partition==part&&!x.Match);
            if(t<=0 || t!=expected.EligibleM || a!=expected.TwelveM
                || b!=expected.TwelveU || ids.Length!=expected.EligibleObservations
                || m<a || m>t || u<b || m+u>cap)
                throw new InvalidDataException("M/U físico não reconcilia.");
            var degree=ids.ToDictionary(x=>x,x=>(M:0,U:0));
            foreach(var e in combined.Values.Where(x=>x.Partition==part))
                foreach(var id in new[]{e.A,e.B})
                {
                    var prev=degree[id];
                    degree[id]=(checked(prev.M+1),checked(prev.U+(e.Match?0:1)));
                }
            var all=degree.Values.Select(x=>x.M).Order().ToArray();
            var falseOnly=degree.Values.Select(x=>x.U).Order().ToArray();
            if(all.Sum(x=>(long)x)!=2*(m+u) || falseOnly.Sum(x=>(long)x)!=2*u)
                throw new InvalidDataException("Grau de U não concilia.");
            static int P(int[] v,int n)=>v[(int)((v.LongLength*n+99)/100)-1];
            result.Add(new Split(part,t,a,b,m-a,u-b,m,u,decimal.Divide(m,t),
                Math.Max(0,checked((19*t+19)/20)-m),ids.Length,
                all.Count(x=>x==0),P(all,95),P(all,99),all[^1],
                P(falseOnly,95),P(falseOnly,99),falseOnly[^1]));
        }
        return result;
    }
}
