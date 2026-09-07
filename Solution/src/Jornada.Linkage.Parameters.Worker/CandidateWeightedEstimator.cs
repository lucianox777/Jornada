using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Diagnostic distributions only. Not an operational model or a canonical parameter set.</summary>
public sealed record CandidateWeightedDistribution(string Feature, decimal ObservedWeight,
    decimal EffectiveSampleSize, IReadOnlyDictionary<string, decimal> Probabilities);

public sealed record CandidateWeightedClass(IndependentMatchLabel Label, int PairCount, int IndependentGroups,
    decimal DesignWeight, decimal EffectiveSampleSize, IReadOnlyList<CandidateWeightedDistribution> Distributions);

public sealed record CandidateWeightedDiagnostics(string Version, string FrameFingerprint,
    string SelectionFingerprint, string LabelingReference, string FeatureVersion, decimal SmoothingAlpha,
    int TrainingPairs, int EvaluationPairs, IReadOnlyList<CandidateWeightedClass> TrainingClasses);

public static class CandidateWeightedEstimator
{
    public const string Version = "CANDIDATE_WEIGHTED_DIAGNOSTIC_V1";
    private static readonly string[] Names = ["EXACT", "HIGH", "MEDIUM", "LOW", "MISSING"];
    private static readonly string[] Birth = ["000", "001", "010", "011", "100", "101", "110", "111", "MISSING"];

    public static CandidateWeightedDiagnostics Estimate(ValidatedCandidateCorpus corpus, decimal alpha,
        decimal minimumEffectiveSampleSize = 2m)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        if (alpha <= 0m || alpha > 1000m || minimumEffectiveSampleSize < 1m)
            throw new ArgumentOutOfRangeException(nameof(alpha));
        var rows = corpus.Observations;
        if (rows.Any(r => r.Label == IndependentMatchLabel.Inconclusive))
            throw new InvalidOperationException("Rótulos inconclusivos exigem nova adjudicação ou metodologia de não resposta validada.");
        var training = rows.Where(r => r.Partition == CandidateCorpusPartition.Training).ToArray();
        var evaluation = rows.Where(r => r.Partition == CandidateCorpusPartition.Evaluation).ToArray();
        if (training.Length == 0 || evaluation.Length == 0 ||
            training.Select(r => r.IndependenceGroupId).Intersect(evaluation.Select(r => r.IndependenceGroupId)).Any())
            throw new InvalidOperationException("Treinamento e avaliação independentes são obrigatórios.");
        var classes = new List<CandidateWeightedClass>();
        foreach (var label in new[] { IndependentMatchLabel.Match, IndependentMatchLabel.NonMatch })
        {
            var selected = training.Where(r => r.Label == label).ToArray();
            if (selected.Length == 0 || selected.Select(r => r.IndependenceGroupId).Distinct().Count() < 2 ||
                !evaluation.Any(r => r.Label == label))
                throw new InvalidOperationException("Classes exigem grupos independentes e cobertura na avaliação.");
            var total = selected.Sum(r => r.Sample.DesignWeight);
            var squares = selected.Sum(r => r.Sample.DesignWeight * r.Sample.DesignWeight);
            var effective = total * total / squares;
            if (effective < minimumEffectiveSampleSize)
                throw new InvalidOperationException("Tamanho efetivo insuficiente para o diagnóstico solicitado.");
            var distributions = new[]
            {
                Distribution("NOME", Names, selected, r => NameState(r.Comparison.Name), alpha),
                Distribution("NOME_MAE", Names, selected, r => NameState(r.Comparison.Mother), alpha),
                Distribution("NASCIMENTO_CONJUNTO", Birth, selected, r => BirthState(r.Comparison.BirthAgreementMask), alpha)
            };
            classes.Add(new CandidateWeightedClass(label, selected.Length,
                selected.Select(r => r.IndependenceGroupId).Distinct().Count(), total, effective,
                Array.AsReadOnly(distributions)));
        }
        return new CandidateWeightedDiagnostics(Version, corpus.Capture.FrameFingerprint,
            corpus.Capture.SelectionFingerprint, corpus.Manifest.Reference, CandidateLabeling.FeatureVersion, alpha,
            training.Length, evaluation.Length, Array.AsReadOnly(classes.ToArray()));
    }

    private static CandidateWeightedDistribution Distribution(string feature, IReadOnlyList<string> states,
        IReadOnlyList<ValidatedCandidateObservation> rows,
        Func<ValidatedCandidateObservation, string> state, decimal alpha)
    {
        var sums = states.ToDictionary(s => s, _ => 0m, StringComparer.Ordinal);
        var squares = states.ToDictionary(s => s, _ => 0m, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = state(row);
            var weight = row.Sample.DesignWeight;
            sums[key] += weight;
            squares[key] += weight * weight;
        }
        var total = sums.Values.Sum();
        var denominator = total + alpha * states.Count;
        var probabilities = states.ToDictionary(s => s, s => (sums[s] + alpha) / denominator, StringComparer.Ordinal);
        // The effective sample size is based on all rows, including missing evidence.
        var squareTotal = squares.Values.Sum();
        return new CandidateWeightedDistribution(feature, total, total * total / squareTotal,
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, decimal>(probabilities));
    }

    private static string NameState(NameComparisonState? state) => state?.ToString() ?? "MISSING";
    private static string BirthState(byte? mask) => mask is { } value
        ? Birth[value] : "MISSING";
}
