namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Isolated SQL 15th-pass diagnostic: person prefix3 AND mother prefix3,
/// with physically missing DOB in at least one observation. Never scores or
/// promotes a probabilistic link. Measures incremental M/U only in TRAIN/VALIDATION.
/// </summary>
public static class V9NameMotherMissingBirthSqlAudit
{
    public const string Version = "V9_SQL_NAME3_MOTHER3_ANY_DOB_MISSING_TRAIN_VALIDATION_V1";
    public const string Status = "DIAGNOSTIC_ONLY_UNPUBLISHED_NO_TEST_NO_PROMOTION";

    public sealed record Split(
        string Partition, long AllNoCpfTruthM, long NameEligibleTruthM,
        long TruthMWithMissingSqlBirth, long PreviouslyRecoveredEligibleM,
        long PreviouslyExposedEligibleU, long AddedEligibleM, long AddedEligibleU,
        long AddedIneligibleM, long AddedIneligibleU, long NewlyRecoveredMissingBirthM,
        long TotalEligibleRecoveredM, long TotalEligibleExposedU,
        long RemainingUnrecoveredEligibleM, long MoreMNeededFor95Percent,
        decimal? RecallEligibleAfter, decimal? RecallAgainstAllTruthAfter,
        decimal? MissingBirthMRecall, long AddedPhysicalPairs);

    public sealed record Report(
        string Version, string Status, string Scope, bool MayPromotePolicy,
        bool FdrMeasured, IReadOnlyList<Split> Splits);

    public static Report Analyze(
        IReadOnlyList<V9SqlTwoPrefix3Probe.Observation> observations,
        IReadOnlyCollection<V9SqlNamePrefix3BirthAudit.Pair> sqlMissingBirth,
        V9TwoFieldSqlCoverageAudit.Report legacy,
        int hardPairCap)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(sqlMissingBirth);
        ArgumentNullException.ThrowIfNull(legacy);
        if (hardPairCap is < 1 or > 1_000_000
            || legacy.NameRequiredPolicyVersion !=
                V9MinimumEvidenceEligibility.PersonRequiredVersion
            || legacy.NameRequiredSplits.Count != 2
            || legacy.Splits.Count != 2
            || legacy.MayPromoteModelOrBlockingPolicy
            || legacy.FdrMeasured)
            throw new InvalidDataException("Contrato baseline de nome obrigatório inválido.");

        var all = new Dictionary<int, V9SqlTwoPrefix3Probe.Observation>();
        var usable = new Dictionary<int, bool>();
        foreach (var x in observations)
        {
            ArgumentNullException.ThrowIfNull(x);
            if (x.Id < 0 || x.Partition is not ("TRAIN" or "VALIDATION")
                || string.IsNullOrWhiteSpace(x.Base)
                || string.IsNullOrWhiteSpace(x.Gestor)
                || !all.TryAdd(x.Id, x))
                throw new InvalidDataException("Observação duplicada/inválida ou TEST vazado.");
            usable.Add(x.Id, x.Cpf is null &&
                V9MinimumEvidenceEligibility.EvaluatePersonRequired(
                    x.PersonFull, x.MotherFull,
                    x.Birth?.ToDateTime(TimeOnly.MinValue)).Eligible);
        }

        var truth = new Dictionary<string, (long Total, HashSet<(int A,int B)> Missing)>(
            StringComparer.Ordinal)
        {
            ["TRAIN"] = (0,new HashSet<(int,int)>()),
            ["VALIDATION"] = (0,new HashSet<(int,int)>())
        };
        foreach (var group in all.Values.Where(x=>x.Cpf is null)
                     .GroupBy(x=>(x.Partition,x.Base)))
        {
            var rows=group.ToArray();
            for (var i=0;i<rows.Length;i++)
            for (var j=i+1;j<rows.Length;j++)
            {
                var a=rows[i];
                var b=rows[j];
                if (a.Gestor==b.Gestor || !usable[a.Id] || !usable[b.Id])
                    continue;
                var v=truth[group.Key.Partition];
                v.Total++;
                if (!a.Birth.HasValue || !b.Birth.HasValue)
                {
                    if (!v.Missing.Add((Math.Min(a.Id,b.Id),Math.Max(a.Id,b.Id))))
                        throw new InvalidDataException("Gabarito M de DOB ausente duplicado.");
                }
                truth[group.Key.Partition]=v;
            }
        }

        var sql = new HashSet<(int A,int B)>();
        var counts = new Dictionary<string, (long EM,long EU,long IM,long IU)>(
            StringComparer.Ordinal)
        {
            ["TRAIN"]=(0,0,0,0),
            ["VALIDATION"]=(0,0,0,0)
        };
        foreach(var edge in sqlMissingBirth)
        {
            ArgumentNullException.ThrowIfNull(edge);
            if (edge.A<0 || edge.A>=edge.B
                || edge.Partition is not ("TRAIN" or "VALIDATION")
                || !all.TryGetValue(edge.A,out var a)
                || !all.TryGetValue(edge.B,out var b)
                || edge.Partition!=a.Partition || edge.Partition!=b.Partition
                || a.Gestor==b.Gestor || a.Cpf is not null || b.Cpf is not null
                || (a.Birth.HasValue && b.Birth.HasValue)
                || a.Person3 is null || a.Person3!=b.Person3
                || a.Mother3 is null || a.Mother3!=b.Mother3
                || (a.Base==b.Base)!=edge.Match
                || !sql.Add((edge.A,edge.B)))
                throw new InvalidDataException("Pass SQL P3+M3 sem DOB inválido/duplicado/TEST.");
            if(sql.Count>hardPairCap)
                throw new InvalidDataException("Cap 1 milhão excedido; abortar sem truncar.");
            var c=counts[edge.Partition];
            if(usable[edge.A] && usable[edge.B])
            {
                if(edge.Match)
                {
                    if(!truth[edge.Partition].Missing.Contains((edge.A,edge.B)))
                        throw new InvalidDataException("SQL M não pertence ao gabarito de DOB ausente.");
                    c.EM++;
                }
                else c.EU++;
            }
            else
            {
                if(edge.Match)c.IM++;
                else c.IU++;
            }
            counts[edge.Partition]=c;
        }

        var splits=new List<Split>(2);
        foreach(var part in new[]{"TRAIN","VALIDATION"})
        {
            var baseline=legacy.NameRequiredSplits.Single(x=>x.Partition==part);
            var overall=legacy.Splits.Single(x=>x.Partition==part);
            var t=truth[part];
            var c=counts[part];
            if(t.Total!=baseline.EligibleTruthM
                || t.Missing.Count!=baseline.EligibleTruthMWithoutSqlBirth
                || overall.AllNoCpfTruthM!=baseline.EligibleTruthM+baseline.ExcludedTruthM
                || c.EM>t.Missing.Count
                || baseline.SqlEligibleM+c.EM>t.Total)
                throw new InvalidDataException("Novo SQL não concilia com gabarito pré-SQL.");
            var afterM=baseline.SqlEligibleM+c.EM;
            var afterU=baseline.SqlEligibleU+c.EU;
            var min95=(19*t.Total+19)/20;
            splits.Add(new Split(part,overall.AllNoCpfTruthM,t.Total,
                t.Missing.Count,baseline.SqlEligibleM,baseline.SqlEligibleU,
                c.EM,c.EU,c.IM,c.IU,c.EM,afterM,afterU,t.Total-afterM,
                Math.Max(0,min95-afterM),
                t.Total==0 ? null : decimal.Divide(afterM,t.Total),
                overall.AllNoCpfTruthM==0 ? null :
                    decimal.Divide(afterM,overall.AllNoCpfTruthM),
                t.Missing.Count==0 ? null : decimal.Divide(c.EM,t.Missing.Count),
                c.EM+c.EU+c.IM+c.IU));
        }
        return new Report(Version,Status,"TRAIN_VALIDATION_ONLY_TEST_BLIND",
            false,false,Array.AsReadOnly(splits.ToArray()));
    }
}
