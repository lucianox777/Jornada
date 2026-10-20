using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record V9TrainingCandidate(V9FittedSnapshot Snapshot, V9PhysicalSourceResolveMetric Metric,
    V9FittedJointSupportAudit.Report Support, double? P95Milliseconds, double FalseDegreeP99,
    IReadOnlyList<string> Rejections);

public sealed record V9TrainingRiskPoint(double MaximumFdr, string? SnapshotSha256, double? Recall, double? Fdr);
public sealed record V9TrainingBatchProgress(int Batch, int NewConfigurations, int TotalConfigurations,
    IReadOnlyList<V9TrainingRiskPoint> Bands, double MaximumRecallGain, double MaximumFdrGain,
    double? RecallResolution, double? FdrResolution, bool MaterialGain, int ConsecutiveStagnantBatches,
    string UncertaintyScope = "PAIRED_IDENTITY_BOOTSTRAP_HEURISTIC_NOT_IID_CERTIFICATION",
    double MaximumGainInResolutionUnits = 0);

/// <summary>TRAIN_SELECTION only: constrained observed frontier, not holdout certification.</summary>
public static class V9TrainingParetoSelection
{
    public static IReadOnlyList<V9TrainingCandidate> Frontier(IEnumerable<V9TrainingCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var feasible = candidates.Where(c => c.Rejections.Count == 0 && c.Metric.FdrAfterResolve.HasValue)
            .OrderBy(c => c.Snapshot.ContentSha256, StringComparer.Ordinal).ToArray();
        foreach (var c in feasible)
        {
            var m = c.Metric;
            if (m.SyntheticGoldOracleGrouping || m.CandidateGrouping != "BLIND_FS_COMPLETE_LINK_CANDIDATES"
                || m.EligibleSourceObservationsBeforeBlocking <= 0 || m.TrueAcceptedSourceObservations < 0
                || m.TrueAcceptedSourceObservations > m.EligibleSourceObservationsBeforeBlocking
                || m.FalseAcceptedSourceObservations < 0
                || m.TrueAcceptedSourceObservations > long.MaxValue - m.FalseAcceptedSourceObservations
                || m.TrueAcceptedSourceObservations + m.FalseAcceptedSourceObservations == 0
                || !double.IsFinite(m.GlobalSourceRecall) || !double.IsFinite(m.FdrAfterResolve!.Value)
                || Math.Abs(m.GlobalSourceRecall - (double)m.TrueAcceptedSourceObservations / m.EligibleSourceObservationsBeforeBlocking) > 1e-12
                || Math.Abs(m.FdrAfterResolve!.Value - (double)m.FalseAcceptedSourceObservations
                    / (m.TrueAcceptedSourceObservations + m.FalseAcceptedSourceObservations)) > 1e-12)
                throw new InvalidDataException("Fronteira interna exige métricas reconciliadas e agrupamento sem ORACLE.");
        }
        return Array.AsReadOnly(feasible.Where(p => !feasible.Any(q =>
            q.Metric.GlobalSourceRecall >= p.Metric.GlobalSourceRecall && q.Metric.FdrAfterResolve <= p.Metric.FdrAfterResolve
            && (q.Metric.GlobalSourceRecall > p.Metric.GlobalSourceRecall || q.Metric.FdrAfterResolve < p.Metric.FdrAfterResolve)))
            .GroupBy(c => (c.Metric.TrueAcceptedSourceObservations, c.Metric.FalseAcceptedSourceObservations,
                c.Metric.EligibleSourceObservationsBeforeBlocking)).Select(g => g.First())
            .OrderBy(c => c.Metric.GlobalSourceRecall).ThenBy(c => c.Metric.FdrAfterResolve)
            .ThenBy(c => c.Snapshot.ContentSha256, StringComparer.Ordinal).ToArray());
    }

    public static IReadOnlyList<V9TrainingCandidate> Finalists(IReadOnlyList<V9TrainingCandidate> frontier,
        IReadOnlyList<double> riskCeilings, double operationalRisk)
    {
        ArgumentNullException.ThrowIfNull(frontier);
        ArgumentNullException.ThrowIfNull(riskCeilings);
        if (frontier.Count == 0) return [];
        // First preserve the highest observed recall within operational epsilon;
        // then represent a safer band and the normalized ideal-distance choice.
        // All choices and tie-breaks are made BEFORE seeing external VALIDATION.
        var chosen = new List<V9TrainingCandidate>();
        void Add(V9TrainingCandidate? candidate)
        {
            if (candidate is not null && chosen.All(x => x.Snapshot.ContentSha256 != candidate.Snapshot.ContentSha256)) chosen.Add(candidate);
        }
        V9TrainingCandidate? Best(double ceiling) => frontier.Where(c => c.Metric.FdrAfterResolve <= ceiling)
            .OrderByDescending(c => c.Metric.GlobalSourceRecall).ThenBy(c => c.Metric.FdrAfterResolve)
            .ThenBy(c => c.Snapshot.ContentSha256, StringComparer.Ordinal).FirstOrDefault();
        Add(Best(operationalRisk));
        foreach (var ceiling in riskCeilings.Where(x => x < operationalRisk).OrderByDescending(x => x))
        {
            Add(Best(ceiling));
            if (chosen.Count == 2) break;
        }
        var minR = frontier.Min(c => c.Metric.GlobalSourceRecall); var maxR = frontier.Max(c => c.Metric.GlobalSourceRecall);
        var minF = frontier.Min(c => c.Metric.FdrAfterResolve!.Value); var maxF = frontier.Max(c => c.Metric.FdrAfterResolve!.Value);
        var ideal = frontier.OrderBy(c =>
            Math.Pow(maxR == minR ? 0 : (maxR - c.Metric.GlobalSourceRecall) / (maxR - minR), 2)
            + Math.Pow(maxF == minF ? 0 : (c.Metric.FdrAfterResolve!.Value - minF) / (maxF - minF), 2))
            .ThenBy(c => c.Snapshot.ContentSha256, StringComparer.Ordinal).ToArray();
        foreach (var candidate in ideal) { if (chosen.Count == 3) break; Add(candidate); }
        return chosen.AsReadOnly();
    }

    public static IReadOnlyList<V9TrainingRiskPoint> Bands(IReadOnlyList<V9TrainingCandidate> frontier,
        IReadOnlyList<double> ceilings) => Array.AsReadOnly(ceilings.OrderBy(x => x).Select(ceiling =>
        {
            var best = frontier.Where(c => c.Metric.FdrAfterResolve <= ceiling)
                .OrderByDescending(c => c.Metric.GlobalSourceRecall).ThenBy(c => c.Metric.FdrAfterResolve)
                .ThenBy(c => c.Snapshot.ContentSha256, StringComparer.Ordinal).FirstOrDefault();
            return new V9TrainingRiskPoint(ceiling, best?.Snapshot.ContentSha256,
                best?.Metric.GlobalSourceRecall, best?.Metric.FdrAfterResolve);
        }).ToArray());

    public static (double Recall, double Fdr) PairedBootstrapResolution(V9TrainingCandidate current,
        V9TrainingCandidate? previous, int repetitions, int seed, double minimumRecall, double minimumFdr)
    {
        if (repetitions < 100 || !double.IsFinite(minimumRecall) || minimumRecall <= 0
            || !double.IsFinite(minimumFdr) || minimumFdr <= 0)
            throw new InvalidDataException("Bootstrap exige repetições e resoluções práticas positivas pré-declaradas.");
        var a = current.Metric.IdentityOutcomes?.OrderBy(x => x.BaseIdentity, StringComparer.Ordinal).ToArray()
            ?? throw new InvalidDataException("Bootstrap exige contagens completas por identidade.");
        var b = previous?.Metric.IdentityOutcomes?.OrderBy(x => x.BaseIdentity, StringComparer.Ordinal).ToArray() ?? a;
        if (a.Length < 2 || !a.Select(x => (x.BaseIdentity, x.EligibleSources)).SequenceEqual(b.Select(x => (x.BaseIdentity, x.EligibleSources)))
            || a.Concat(b).Any(x => x.EligibleSources < 0 || x.TrueAccepted < 0 || x.FalseAccepted < 0 || x.TrueAccepted > x.EligibleSources))
            throw new InvalidDataException("Bootstrap pareado exige a mesma coorte e denominadores.");
        var random = new Random(seed);
        var recall = new List<double>(); var fdr = new List<double>();
        for (var iteration = 0; iteration < repetitions; iteration++)
        {
            long at = 0, af = 0, bt = 0, bf = 0, truth = 0;
            for (var i = 0; i < a.Length; i++)
            {
                var j = random.Next(a.Length);
                at += a[j].TrueAccepted; af += a[j].FalseAccepted;
                bt += b[j].TrueAccepted; bf += b[j].FalseAccepted; truth += a[j].EligibleSources;
            }
            if (truth > 0) recall.Add(previous is null ? (double)at / truth : (double)(at - bt) / truth);
            if (at + af > 0 && bt + bf > 0)
                fdr.Add((double)af / (at + af) - (previous is null ? 0 : (double)bf / (bt + bf)));
        }
        static double Sd(List<double> values)
        {
            if (values.Count < 2) return double.PositiveInfinity;
            var mean = values.Average();
            return Math.Sqrt(values.Sum(x => (x - mean) * (x - mean)) / (values.Count - 1));
        }
        var eligible = a.Sum(x => x.EligibleSources);
        var accepted = a.Sum(x => x.TrueAccepted + x.FalseAccepted);
        return (Math.Max(Sd(recall), Math.Max(minimumRecall, eligible > 0 ? 1d / eligible : double.PositiveInfinity)),
            Math.Max(Sd(fdr), Math.Max(minimumFdr, accepted > 0 ? 1d / accepted : double.PositiveInfinity)));
    }
}
