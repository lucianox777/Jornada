using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public const string FrozenVersion = "V9_SYNTHETIC_FROZEN_JOINT_FS_FROM_SEED42_V1";
    public const string FitLocally = "FIT_TRAIN_AND_VALIDATION_FOR_SOURCE_SEED42";
    public const string Transferred = "TRANSFER_SOURCE_SEED42_UNCHANGED_NO_REFIT_OR_RETHRESHOLD";
    public static string Digest(SyntheticV9FrozenModelV1 model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(model))));
    }

    private static void RequireValidFrozen(SyntheticV9FrozenModelV1 model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Version != FrozenVersion || model.SourceSeed != 42UL
            || string.IsNullOrWhiteSpace(model.SourceInputFingerprint)
            || model.Cells is not { Length: 81 } || model.Parameters is null
            || model.ThresholdLogOdds is < -1000m or > 1000m)
            throw new InvalidOperationException("Modelo FS V9 congelado tem contrato ou proveniência inválida.");
        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        if (model.Cells.Any(c => c is null || !Enum.IsDefined(c.Person)
                            || !Enum.IsDefined(c.Mother) || c.M <= 0 || c.U <= 0)
            || model.Cells.Select(c => (c.Person, c.Mother)).Distinct().Count() != levels.Length * levels.Length
            || Math.Abs(model.Cells.Sum(c => c.M) - 1m) > 0.0000001m
            || Math.Abs(model.Cells.Sum(c => c.U) - 1m) > 0.0000001m)
            throw new InvalidOperationException("Modelo FS V9 congelado exige 81 células conjuntas válidas.");
        // Shared production C# scorer validates the statistical parameters.
        _ = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
            model.Parameters, "Maria Silva", "Maria Silva", "Ana Costa", "Ana Costa",
            model.Cells, CompositeNominalComparisonV9.ContractVersion,
            leftBirthDate: new DateOnly(2000, 1, 1),
            rightBirthDate: new DateOnly(2000, 1, 1));
    }
    private sealed record Pair(SyntheticObservation A, SyntheticObservation B, bool Match, bool Retrieved);
    private sealed record Scored(bool Match, bool Retrieved, decimal? LogOdds);

    public static SyntheticV9ProbeReport Analyze(
        IReadOnlyList<SyntheticPerson> people, IReadOnlyList<SyntheticObservation> observations,
        ulong seed, string fingerprint,
        SyntheticV9FrozenModelV1? externalFrozenModel = null)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        if (externalFrozenModel is not null)
        {
            RequireValidFrozen(externalFrozenModel);
            if (seed == externalFrozenModel.SourceSeed
                || string.Equals(fingerprint, externalFrozenModel.SourceInputFingerprint,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Teste transferido exige seed e corpus diferentes da origem.");
        }
        else if (seed != 42UL)
        {
            // This experiment freezes a SINGLE source model on seed 42.
            // Seed 43/44 must not silently refit their own TRAIN/VALIDATION.
            throw new InvalidOperationException("Experimento V9 requer modelo da seed 42 para seeds externas.");
        }
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
        // Diagnostics of local support above are intentionally descriptive
        // for transferred corpora; their fitted parameters NEVER enter scoring.
        var scoringCells = externalFrozenModel?.Cells ?? cells;
        var scoringParameters = externalFrozenModel?.Parameters ?? parameters;
        Scored[] Score(string partition) => frames[partition].Select(p =>
        {
            if (!p.Retrieved)
            {
                if (!p.Match) throw new InvalidOperationException("U fora da amostra recuperada.");
                return new Scored(true,false,null);
            }
            var outcome = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                scoringParameters,p.A.Name,p.B.Name,p.A.MotherName,p.B.MotherName,
                scoringCells,CompositeNominalComparisonV9.ContractVersion,
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
        var threshold=externalFrozenModel?.ThresholdLogOdds
            ?? negatives[(int)decimal.Floor(negatives.Length*FprBudget)];
        var validation=Metrics("VALIDATION",valScored,threshold);
        // A transferred model is measured AS-IS: do not re-fit or retune
        // even if its target VALIDATION exceeds the original error budget.
        if (externalFrozenModel is null && validation.Fpr>FprBudget)
            throw new InvalidOperationException("FPR VALIDATION acima de 1%.");
        var test=Metrics("TEST",Score("TEST"),threshold);
        var pass=test.Tp>0 && test.Recall>=MinRecall && test.Fpr<=FprBudget;
        var frozen = externalFrozenModel ?? new SyntheticV9FrozenModelV1(
            FrozenVersion, seed, fingerprint, threshold, cells, parameters);
        RequireValidFrozen(frozen);
        return new SyntheticV9ProbeReport(
            Version,Scope,seed,fingerprint,people.Count,observations.Count,
            "REAL_CSHARP_FS_PREVIEW_SINGLE_JOINT_NOMINAL_AND_BIRTH",
            "TF_WEIGHT_ZERO_IN_THIS_EXPERIMENT_NOT_FULL_TF_CALIBRATION",
            Alpha,FprBudget,MinRecall,m,u,
            frames["TRAIN"].Count(x=>x.Match && !x.Retrieved),
            counts.Values.Count(x=>x.M==0 || x.U==0),
            threshold,validation,test,
            pass?"PASS_ON_SAMPLED_SYNTHETIC_PROXY":"FAIL_ON_SAMPLED_SYNTHETIC_PROXY",
            "NOT_PROVEN_ON_REAL_SQL_BLOCKING_OR_MUNICIPAL_POPULATION")
        {
            FrozenModel = frozen,
            FrozenModelSha256 = Digest(frozen),
            CalibrationMode = externalFrozenModel is null ? FitLocally : Transferred
        };
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
    string SyntheticProxyVerdict,string OperationalConclusion)
{
    // Aggregate parameters only; NO PII. The same source model/digest must
    // be bit-for-bit identical in the two independent target corpus reports.
    public SyntheticV9FrozenModelV1? FrozenModel { get; init; }
    public string? FrozenModelSha256 { get; init; }
    public string? CalibrationMode { get; init; }
}

public sealed record SyntheticV9FrozenModelV1(
    string Version, ulong SourceSeed, string SourceInputFingerprint,
    decimal ThresholdLogOdds, FellegiSunterJointNominalCellV9[] Cells,
    IReadOnlyDictionary<string, decimal> Parameters);
