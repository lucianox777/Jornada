using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.SyntheticCorpus;

/// <summary>
/// Executable V9 FS C# on 30k synthetic identities, strictly divided by
/// BasePersonId. Candidate proxy is NOT the real SQL VERSIONED_ALIAS union.
/// No operational or population certification, no V8 comparison.
/// </summary>
public static class SyntheticV9FunctionalProbe
{
    public const string Version = "V9_30000_FS_CSHARP_FUNCTIONAL_PROBE_V1";
    public const string Scope = "FIRST_INITIAL_PROXY_NOT_REAL_SQL_BLOCKING";
    private const decimal Alpha = 0.5m;
    private const decimal FprBudget = 0.01m;
    private const decimal MinRecall = 0.80m;
    private sealed record Pair(SyntheticObservation A, SyntheticObservation B, bool Match, bool Retrieved);
    private sealed record Scored(bool Match, bool Retrieved, decimal? LogOdds);

    public static SyntheticV9ProbeReport Analyze(
        IReadOnlyList<SyntheticPerson> people, IReadOnlyList<SyntheticObservation> observations,
        ulong seed, string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        if (people.Count == 0 || observations.Count == 0
            || people.Select(x => x.BasePersonId).Distinct(StringComparer.Ordinal).Count() != people.Count)
            throw new InvalidOperationException("Gabarito vazio ou bases duplicadas.");
        var truth = people.ToDictionary(x => x.BasePersonId, x => x.Partition, StringComparer.Ordinal);
        if (truth.Values.Any(x => x is not ("TRAIN" or "VALIDATION" or "TEST"))
            || observations.Any(x => !truth.TryGetValue(x.BasePersonId, out var p) || p != x.Partition))
            throw new InvalidOperationException("Violação das partições por identidade-base.");
        var groups = observations.GroupBy(x => x.BasePersonId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key,
                x => x.OrderBy(y => y.ObservationId, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var frames = new Dictionary<string, Pair[]>(StringComparer.Ordinal);
        foreach (var partition in new[] { "TRAIN", "VALIDATION", "TEST" })
        {
            var pairs = new List<Pair>();
            var representatives = new List<SyntheticObservation>();
            foreach (var person in people.Where(x => x.Partition == partition)
                         .OrderBy(x => x.BasePersonId, StringComparer.Ordinal))
            {
                if (!groups.TryGetValue(person.BasePersonId, out var rows) || rows.Length == 0)
                    continue;
                representatives.Add(rows[0]);
                var second = rows.FirstOrDefault(x => x.Gestor != rows[0].Gestor);
                if (second is not null) pairs.Add(Make(rows[0], second));
            }
            // U: distinct identities, limited deterministic same-first-initial
            // neighbors. Not the eligible SQL candidate universe.
            foreach (var bucket in representatives.GroupBy(x => Initial(x.Name), StringComparer.Ordinal))
            {
                var rows = bucket.OrderBy(x => x.BasePersonId, StringComparer.Ordinal).ToArray();
                for (var i = 1; i < rows.Length; i++) pairs.Add(Make(rows[i-1], rows[i]));
            }
            if (!pairs.Any(x => x.Match) || !pairs.Any(x => !x.Match))
                throw new InvalidOperationException("Partição sem pares M/U.");
            frames.Add(partition, pairs.ToArray());
        }
        var train = frames["TRAIN"].Where(x => x.Retrieved).ToArray();
        var m = train.Count(x => x.Match);
        var u = train.Length - m;
        if (m == 0 || u == 0) throw new InvalidOperationException("M/U TRAIN não elegíveis.");
        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        var counts = levels.SelectMany(a => levels.Select(b => (a,b)))
            .ToDictionary(x => x, _ => (M:0,U:0));
        foreach (var p in train)
        {
            var key = (CompositeNominalComparisonV9.Classify(p.A.Name,p.B.Name),
                CompositeNominalComparisonV9.Classify(p.A.MotherName,p.B.MotherName));
            var old = counts[key];
            counts[key] = p.Match ? (old.M+1,old.U) : (old.M,old.U+1);
        }
        var cells = levels.SelectMany(a => levels.Select(b =>
        {
            var n = counts[(a,b)];
            return new FellegiSunterJointNominalCellV9(a,b,
                (n.M+Alpha)/(m+81m*Alpha), (n.U+Alpha)/(u+81m*Alpha));
        })).ToArray();
        // Validates 81 normalized M/U probabilities inside real scorer.
        _ = FellegiSunterScoring.CalculateCompositeNominalJointV9(
            "Maria Silva","Maria Silva","Ana Costa","Ana Costa",
            cells,CompositeNominalComparisonV9.ContractVersion);

        var mb = train.Where(p => p.Match && p.A.BirthDate.HasValue && p.B.BirthDate.HasValue).ToArray();
        var ub = train.Where(p => !p.Match && p.A.BirthDate.HasValue && p.B.BirthDate.HasValue).ToArray();
        if (mb.Length == 0 || ub.Length == 0) throw new InvalidOperationException("Nascimento M/U sem suporte.");
        var bm = (mb.Count(p => p.A.BirthDate == p.B.BirthDate)+Alpha)/(mb.Length+2m*Alpha);
        var bu = (ub.Count(p => p.A.BirthDate == p.B.BirthDate)+Alpha)/(ub.Length+2m*Alpha);
        var parameters = new Dictionary<string,decimal>(StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = decimal.Divide(m,m+u),
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.NeutralMissingEvidenceScoring] = 1m,
            [LinkageParameterCatalog.BirthSingleEvidenceScoring] = 1m,
            ["M_DATA_NASCIMENTO_EXACT"] = bm,
            ["U_DATA_NASCIMENTO_EXACT"] = bu,
            ["M_DATA_NASCIMENTO_DIFF"] = 1m-bm,
            ["U_DATA_NASCIMENTO_DIFF"] = 1m-bu
        };
        Scored[] Score(string partition) => frames[partition].Select(p =>
        {
            if (!p.Retrieved)
            {
                if (!p.Match) throw new InvalidOperationException("U fora da amostra recuperada.");
                return new Scored(true,false,null);
            }
            var outcome = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                parameters,p.A.Name,p.B.Name,p.A.MotherName,p.B.MotherName,
                cells,CompositeNominalComparisonV9.ContractVersion,
                leftBirthDate:p.A.BirthDate,rightBirthDate:p.B.BirthDate);
            if (outcome.Contributions.Count(x => x.Evidence=="NOMINAL_CONJUNTO_V9")!=1
                || outcome.Contributions.Any(x => x.Evidence is "NOME" or "NOME_MAE"))
                throw new InvalidOperationException("Dupla contagem nominal V9.");
            return new Scored(p.Match,true,outcome.Score.LogOdds);
        }).ToArray();

        var valScored = Score("VALIDATION");
        var negatives = valScored.Where(x=>!x.Match).Select(x=>x.LogOdds!.Value)
            .OrderByDescending(x=>x).ToArray();
        if (negatives.Length < 100) throw new InvalidOperationException("U VALIDATION insuficiente.");
        // Threshold is frozen using validation negatives ONLY. Scores must
        // be strictly greater; ties never count as permitted FPs.
        var threshold=negatives[(int)decimal.Floor(negatives.Length*FprBudget)];
        var validation=Metrics("VALIDATION",valScored,threshold);
        if (validation.Fpr>FprBudget) throw new InvalidOperationException("FPR VALIDATION acima de 1%.");
        var test=Metrics("TEST",Score("TEST"),threshold);
        var pass=test.Tp>0 && test.Recall>=MinRecall && test.Fpr<=FprBudget;
        return new SyntheticV9ProbeReport(
            Version,Scope,seed,fingerprint,people.Count,observations.Count,
            "REAL_CSHARP_FS_PREVIEW_SINGLE_JOINT_NOMINAL_AND_BIRTH",
            "TF_WEIGHT_ZERO_IN_THIS_EXPERIMENT_NOT_FULL_TF_CALIBRATION",
            Alpha,FprBudget,MinRecall,m,u,
            frames["TRAIN"].Count(x=>x.Match && !x.Retrieved),
            counts.Values.Count(x=>x.M==0 || x.U==0),
            threshold,validation,test,
            pass?"PASS_ON_SAMPLED_SYNTHETIC_PROXY":"FAIL_ON_SAMPLED_SYNTHETIC_PROXY",
            "NOT_PROVEN_ON_REAL_SQL_BLOCKING_OR_MUNICIPAL_POPULATION");
    }
    private static SyntheticV9ProbeMetrics Metrics(
        string part,IReadOnlyList<Scored> rows,decimal threshold)
    {
        int tp=0,fp=0,fn=0,tn=0,blocked=0;
        foreach (var row in rows)
        {
            if (row.Match && !row.Retrieved) { blocked++; fn++; continue; }
            var predicted=row.LogOdds!.Value>threshold;
            if (row.Match && predicted) tp++;
            else if (row.Match) fn++;
            else if (predicted) fp++;
            else tn++;
        }
        if (tp+fn==0 || fp+tn==0) throw new InvalidOperationException("M/U TEST inválido.");
        var positives=tp+fp;
        return new SyntheticV9ProbeMetrics(part,tp,fp,fn,tn,blocked,
            decimal.Divide(tp,tp+fn),decimal.Divide(fp,fp+tn),
            positives==0?null:decimal.Divide(tp,positives),
            positives==0?null:decimal.Divide(fp,positives));
    }
    private static Pair Make(SyntheticObservation a,SyntheticObservation b)
    {
        if (a.Partition!=b.Partition) throw new InvalidOperationException("Par cruza partições.");
        return new Pair(a,b,a.BasePersonId==b.BasePersonId,Initial(a.Name)==Initial(b.Name));
    }
    private static string Initial(string? name)
    {
        var s=IdentityComparison.NormalizeText(name);
        return string.IsNullOrWhiteSpace(s)?"<MISSING>":s[..1];
    }
}
public sealed record SyntheticV9ProbeMetrics(
    string Partition,int Tp,int Fp,int Fn,int Tn,int MatchedNotRetrieved,
    decimal Recall,decimal Fpr,decimal? Precision,decimal? Fdr);
public sealed record SyntheticV9ProbeReport(
    string Version,string Scope,ulong Seed,string InputFingerprint,int People,int Observations,
    string Scorer,string TermFrequencyStatus,decimal TrainSmoothingAlpha,
    decimal ValidationFprBudget,decimal TestExploratoryRecallMinimum,
    int TrainMatchedRetrieved,int TrainUnmatchedSampled,int TrainMatchedNotRetrieved,
    int TrainCellsWithoutBilateralSupport,decimal FrozenThresholdLogOdds,
    SyntheticV9ProbeMetrics Validation,SyntheticV9ProbeMetrics Test,
    string SyntheticProxyVerdict,string OperationalConclusion);
