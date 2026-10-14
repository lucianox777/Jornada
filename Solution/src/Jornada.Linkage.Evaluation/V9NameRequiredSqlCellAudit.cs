using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Physical SQL candidate-cell census; names are required on both endpoints.
/// SQL equality keys generate candidates; Jaro-Winkler is pairwise diagnostic,
/// NEVER treated as a B-tree key or as a calibrated decision.
/// </summary>
public static class V9NameRequiredSqlCellAudit
{
    public const string Version="V9_NAME_REQUIRED_SQL_CELLS_DIAGNOSTIC_V1";
    public const string PersonPhoneticMotherLast="PERSON_PHONETIC_MOTHER_LAST";
    public const string PersonPhoneticYear="PERSON_PHONETIC_BIRTH_YEAR";
    public const string PersonLastMotherPhonetic="PERSON_LAST_MOTHER_PHONETIC";
    public const string PersonPhoneticMotherAny="PERSON_PHONETIC_MOTHER_ANY";
    public static readonly string[] Order=
        [PersonPhoneticMotherLast,PersonPhoneticYear,PersonLastMotherPhonetic,PersonPhoneticMotherAny];

    public sealed record Observation(int Id,string Base,string Partition,string Gestor,
        string? Cpf,string? Person,string? Mother,DateOnly? Birth);
    public sealed record Pass(string Name,IReadOnlyList<V9SqlNamePrefix3BirthAudit.Pair> Pairs);
    public sealed record Cell(string Partition,string Name,long M,long U,
        long NewMvs14,long NewUvs14,long ExclusiveNewM,
        long JaroWinklerHighM,long JaroWinklerHighU);
    public sealed record Split(string Partition,long EligibleTruthM,long LegacyM,long LegacyU,
        long NewM,long NewU,long UnionM,long UnionU,decimal? LegacyRecall,
        decimal? UnionRecall,long MoreMNeededFor95,long MissingBirthTruthM,
        long AbstainingObservations);
    public sealed record Report(string Version,string PolicyVersion,string Scope,
        string ProjectionVersion,string PhoneticVersion,string SurnameSemantics,
        bool MayPromotePolicy,bool FdrMeasured,IReadOnlyList<Cell> Cells,
        IReadOnlyList<Split> Splits);
    public static bool Eligible(Observation x) =>
        x.Cpf is null && V9MinimumEvidenceEligibility.EvaluatePersonRequired(
            x.Person,x.Mother,x.Birth?.ToDateTime(TimeOnly.MinValue)).Eligible;

    public static string? Last(string? fullName) =>
        BrazilianNameComponents.Project(fullName)?.LastContentSurname;

    // Heuristic index BY PRESENCE, not a semantically structured IBGE surname.
    // Last significant surname is promoted to the first/prioritized key only
    // for query ordering; other positions remain searchable (OR, not replacement).
    public static IReadOnlyList<string> AnySurname(string? fullName)
    {
        var components=BrazilianNameComponents.Project(fullName);
        if(components?.LastContentSurname is null)return Array.Empty<string>();
        var tokens=components.NormalizedFull.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var end=components.Agnome is null?tokens.Length:tokens.Length-1;
        var start=components.TitlePrefix is null?1:2;
        var excluded=new HashSet<string>(StringComparer.Ordinal)
            {"DA","DAS","DE","DO","DOS","DI","DU","DEL","D","LA","LAS","LOS"};
        var terms=new HashSet<string>(StringComparer.Ordinal);
        for(int i=start;i<end;i++)
        {
            var token=tokens[i].Trim('.',',');
            if(token.Length>0 && !excluded.Contains(token))terms.Add(token);
        }
        terms.Add(components.LastContentSurname);
        return terms.Order(StringComparer.Ordinal).ToArray();
    }

    public static bool Matches(string cell,Observation a,Observation b)
    {
        var pA=MetaphoneBr.Encode(a.Person);
        var pB=MetaphoneBr.Encode(b.Person);
        var pfon=pA is not null && pA==pB;
        var mLast=Last(a.Mother);
        var pLast=Last(a.Person);
        return cell switch
        {
            PersonPhoneticMotherLast=>pfon && mLast is not null && mLast==Last(b.Mother),
            PersonPhoneticYear=>pfon && a.Birth.HasValue && b.Birth.HasValue
                && V9MinimumEvidenceEligibility.HasBirth(a.Birth.Value.ToDateTime(TimeOnly.MinValue))
                && V9MinimumEvidenceEligibility.HasBirth(b.Birth.Value.ToDateTime(TimeOnly.MinValue))
                && a.Birth.Value.Year==b.Birth.Value.Year,
            PersonLastMotherPhonetic=>pLast is not null && pLast==Last(b.Person)
                && MetaphoneBr.Encode(a.Mother) is { } motherPhonetic
                && motherPhonetic==MetaphoneBr.Encode(b.Mother),
            PersonPhoneticMotherAny=>pfon && AnySurname(a.Mother)
                .Intersect(AnySurname(b.Mother),StringComparer.Ordinal).Any(),
            _=>false
        };
    }

    public static Report Analyze(IReadOnlyList<Observation> observations,
        IEnumerable<V9SqlNamePrefix3BirthAudit.Pair> fourteen,
        IReadOnlyList<Pass> passes,int cap)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(fourteen);
        ArgumentNullException.ThrowIfNull(passes);
        if(cap is <1 or >1_000_000 || passes.Count!=Order.Length ||
            !passes.Select(x=>x.Name).SequenceEqual(Order))
            throw new InvalidDataException("Conjunto/ordem de células inválido.");
        var rows=new Dictionary<int,Observation>();
        foreach(var x in observations)
            if(x.Id<0 || x.Partition is not ("TRAIN" or "VALIDATION")
                || string.IsNullOrWhiteSpace(x.Base) || string.IsNullOrWhiteSpace(x.Gestor)
                || !rows.TryAdd(x.Id,x))
                throw new InvalidDataException("Observação inválida, duplicada ou TEST.");
        var eligible=rows.Values.Where(Eligible).ToDictionary(x=>x.Id);
        var truth=new HashSet<(int,int)>();
        var truthTotals=new Dictionary<string,(long Count,long NoBirth)>
            {["TRAIN"]=(0,0),["VALIDATION"]=(0,0)};
        foreach(var group in eligible.Values.GroupBy(x=>(x.Partition,x.Base)))
        {
            var matches=group.ToArray();
            for(int i=0;i<matches.Length;i++)
            for(int j=i+1;j<matches.Length;j++)
            {
                var a=matches[i];var b=matches[j];
                if(a.Gestor==b.Gestor)continue;
                if(!truth.Add((Math.Min(a.Id,b.Id),Math.Max(a.Id,b.Id))))
                    throw new InvalidDataException("Gabarito repetido.");
                var t=truthTotals[group.Key.Partition];
                t.Count++;
                if(!a.Birth.HasValue || !b.Birth.HasValue)t.NoBirth++;
                truthTotals[group.Key.Partition]=t;
            }
        }
        void Guard(V9SqlNamePrefix3BirthAudit.Pair p)
        {
            if(p.A<0 || p.A>=p.B || p.Partition is not ("TRAIN" or "VALIDATION")
                || !rows.TryGetValue(p.A,out var a) || !rows.TryGetValue(p.B,out var b)
                || a.Partition!=p.Partition || b.Partition!=p.Partition
                || a.Cpf is not null || b.Cpf is not null || a.Gestor==b.Gestor
                || (a.Base==b.Base)!=p.Match)
                throw new InvalidDataException("Par SQL fora do frame/gabarito.");
        }
        var legacy=new Dictionary<(int,int),V9SqlNamePrefix3BirthAudit.Pair>();
        foreach(var p in fourteen)
        {
            Guard(p);
            if(legacy.TryGetValue((p.A,p.B),out var old) && old!=p)
                throw new InvalidDataException("Labels legados conflitantes.");
            legacy.TryAdd((p.A,p.B),p);
            if(legacy.Count>cap)throw new InvalidDataException("Cap antigo.");
        }
        var union=new Dictionary<(int,int),V9SqlNamePrefix3BirthAudit.Pair>(legacy);
        var perPass=new List<HashSet<(int,int)>>();
        foreach(var pass in passes)
        {
            var unique=new HashSet<(int,int)>();
            foreach(var p in pass.Pairs)
            {
                Guard(p);
                if(!eligible.ContainsKey(p.A) || !eligible.ContainsKey(p.B)
                    || !Matches(pass.Name,rows[p.A],rows[p.B])
                    || !unique.Add((p.A,p.B)))
                    throw new InvalidDataException("Passe retornou par inelegível, duplicado ou fora de seu predicado.");
                if(union.TryGetValue((p.A,p.B),out var old) && old!=p)
                    throw new InvalidDataException("Labels de passes conflitantes.");
                union.TryAdd((p.A,p.B),p);
                if(union.Count>cap)throw new InvalidDataException("Cap da união: nunca truncar.");
            }
            perPass.Add(unique);
        }
        var cells=new List<Cell>();
        for(int index=0;index<passes.Count;index++)
        foreach(var partition in new[]{"TRAIN","VALIDATION"})
        {
            var edges=passes[index].Pairs.Where(x=>x.Partition==partition).ToArray();
            var fresh=edges.Where(x=>!legacy.ContainsKey((x.A,x.B))).ToArray();
            cells.Add(new Cell(partition,passes[index].Name,
                edges.LongCount(x=>x.Match),edges.LongCount(x=>!x.Match),
                fresh.LongCount(x=>x.Match),fresh.LongCount(x=>!x.Match),
                fresh.LongCount(x=>x.Match && perPass.Where((_,i)=>i!=index)
                    .All(other=>!other.Contains((x.A,x.B)))),
                edges.LongCount(x=>x.Match && High(x,rows)),
                edges.LongCount(x=>!x.Match && High(x,rows))));
        }
        var result=new List<Split>();
        foreach(var partition in new[]{"TRAIN","VALIDATION"})
        {
            var t=truthTotals[partition];
            var priorM=legacy.Values.LongCount(x=>x.Partition==partition && x.Match
                && eligible.ContainsKey(x.A) && eligible.ContainsKey(x.B));
            var priorU=legacy.Values.LongCount(x=>x.Partition==partition && !x.Match
                && eligible.ContainsKey(x.A) && eligible.ContainsKey(x.B));
            var m=union.Values.LongCount(x=>x.Partition==partition && x.Match
                && eligible.ContainsKey(x.A) && eligible.ContainsKey(x.B));
            var u=union.Values.LongCount(x=>x.Partition==partition && !x.Match
                && eligible.ContainsKey(x.A) && eligible.ContainsKey(x.B));
            if(m>t.Count || priorM>m || priorU>u || t.NoBirth>t.Count)
                throw new InvalidDataException("Gabarito e resultado físico discordam.");
            result.Add(new Split(partition,t.Count,priorM,priorU,m-priorM,u-priorU,m,u,
                t.Count==0?null:decimal.Divide(priorM,t.Count),
                t.Count==0?null:decimal.Divide(m,t.Count),
                Math.Max(0,(19*t.Count+19)/20-m),t.NoBirth,
                rows.Values.LongCount(x=>x.Partition==partition && x.Cpf is null && !Eligible(x))));
        }
        return new Report(Version,V9MinimumEvidenceEligibility.PersonRequiredVersion,
            "TRAIN_VALIDATION_SQL_PHYSICAL_NO_TEST",
            BlockingProjectionKeyProjector.MethodVersion,MetaphoneBr.MethodVersion,
            "IBGE_ANY_SURNAME_PRESENCE_PROXY_LAST_SIGNIFICANT_FIRST_NOT_POSITIONAL_TF",
            false,false,Array.AsReadOnly(cells.ToArray()),Array.AsReadOnly(result.ToArray()));
    }

    private static bool High(V9SqlNamePrefix3BirthAudit.Pair p,
        IReadOnlyDictionary<int,Observation> rows)
    {
        var a=IdentityComparison.NormalizeText(rows[p.A].Person);
        var b=IdentityComparison.NormalizeText(rows[p.B].Person);
        return a is not null && b is not null && IdentityComparison.JaroWinkler(a,b)>=0.92d;
    }
}
