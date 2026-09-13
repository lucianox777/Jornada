namespace Jornada.Linkage.Parameters.Worker;

public sealed record DfCalibrationObservation(
    bool IsTrueMatch,
    NominalDfEvidence Evidence);

public sealed record DfThresholdCandidate(
    string CandidateId,
    double MinimumSimilarity,
    double MinimumTermFrequencyLogAdjustment);

public static class DfThresholdSearch
{
    /// <summary>
    /// Gera uma grade determinística somente a partir de valores observados na validação.
    /// Não cria uma fórmula escalar artificial para combinar distância e frequência.
    /// DF resolve MATCH apenas quando similaridade e evidência TF ultrapassam a fronteira;
    /// todo o restante permanece inconclusivo e segue ao Fellegi-Sunter.
    /// </summary>
    public static IReadOnlyList<DfThresholdCandidate> GenerateCandidates(
        IEnumerable<DfCalibrationObservation> observations,
        int maxSimilarityValues = 32,
        int maxTfValues = 32)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSimilarityValues);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTfValues);

        var materialized = observations
            .Where(x => x.Evidence.TermFrequencyLogAdjustment.HasValue)
            .ToArray();

        var similarities = Thin(
            materialized.Select(x => x.Evidence.Similarity).Distinct().OrderDescending().ToArray(),
            maxSimilarityValues);
        var tfValues = Thin(
            materialized.Select(x => x.Evidence.TermFrequencyLogAdjustment!.Value).Distinct().OrderDescending().ToArray(),
            maxTfValues);

        var result = new List<DfThresholdCandidate>(similarities.Count * tfValues.Count);
        foreach (var similarity in similarities)
        foreach (var tf in tfValues)
        {
            result.Add(new DfThresholdCandidate(
                $"DF|JW>={similarity:R}|TF>={tf:R}",
                similarity,
                tf));
        }

        return result;
    }

    public static CalibrationEvaluation Evaluate(
        DfThresholdCandidate candidate,
        IEnumerable<DfCalibrationObservation> observations)
    {
        long tp = 0, tn = 0, fp = 0, fn = 0, inconclusive = 0, total = 0;

        foreach (var observation in observations)
        {
            total++;
            var tf = observation.Evidence.TermFrequencyLogAdjustment;
            var resolvesMatch = tf.HasValue &&
                                observation.Evidence.Similarity >= candidate.MinimumSimilarity &&
                                tf.Value >= candidate.MinimumTermFrequencyLogAdjustment;

            if (!resolvesMatch)
            {
                inconclusive++;
                continue;
            }

            if (observation.IsTrueMatch) tp++;
            else fp++;
        }

        // DF é deliberadamente assimétrico: não declara NON_MATCH. TN/FN só aparecem
        // no sistema completo após o fallback ao FS; aqui permanecem zero.
        return new CalibrationEvaluation(candidate.CandidateId, tp, tn, fp, fn, inconclusive, total);
    }

    private static IReadOnlyList<double> Thin(IReadOnlyList<double> sorted, int maxValues)
    {
        if (sorted.Count <= maxValues)
            return sorted;
        if (maxValues == 1)
            return new[] { sorted[0] };

        var result = new List<double>(maxValues);
        for (var i = 0; i < maxValues; i++)
        {
            var index = (int)Math.Round(i * (sorted.Count - 1d) / (maxValues - 1d), MidpointRounding.AwayFromZero);
            var value = sorted[index];
            if (result.Count == 0 || result[^1] != value)
                result.Add(value);
        }
        return result;
    }
}
