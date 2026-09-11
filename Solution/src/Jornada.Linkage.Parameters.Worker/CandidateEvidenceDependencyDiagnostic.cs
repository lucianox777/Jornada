using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

public enum CandidateEvidenceDependencyScope
{
    AllStates,
    ObservedOnly
}

public sealed record CandidateEvidenceDependencyMetric(
    IndependentMatchLabel Label,
    CandidateEvidenceDependencyScope Scope,
    string LeftFeature,
    string RightFeature,
    int PairCount,
    int IndependentGroups,
    decimal ObservedWeight,
    decimal EffectiveSampleSize,
    bool IsEstimable,
    string? NonEstimableReason,
    double? TotalVariationDistance,
    double? NormalizedMutualInformation);

public sealed record CandidateEvidenceDependencyReport(
    string Version,
    string FrameFingerprint,
    string SelectionFingerprint,
    string LabelingReference,
    string FeatureVersion,
    int EvaluationPairs,
    IReadOnlyList<CandidateEvidenceDependencyMetric> Metrics,
    string FingerprintSha256);

/// <summary>
/// Diagnóstico somente leitura da hipótese de independência condicional entre as evidências
/// usadas pelo linkage probabilístico. Mede associação empírica na partição Evaluation,
/// separadamente em Match e NonMatch, sem ajustar m/u, posterior, threshold ou decisão.
/// </summary>
public static class CandidateEvidenceDependencyDiagnostic
{
    public const string Version = "CANDIDATE_EVIDENCE_DEPENDENCY_DIAGNOSTIC_V1";

    private static readonly string[] Features = ["NOME", "NOME_MAE", "NASCIMENTO_CONJUNTO"];
    private static readonly string[] BirthStates = ["000", "001", "010", "011", "100", "101", "110", "111"];

    public static CandidateEvidenceDependencyReport Analyze(
        ValidatedCandidateCorpus corpus,
        int minimumIndependentGroups = 2,
        decimal minimumEffectiveSampleSize = 2m)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        if (minimumIndependentGroups < 1)
            throw new ArgumentOutOfRangeException(nameof(minimumIndependentGroups));
        if (minimumEffectiveSampleSize < 1m)
            throw new ArgumentOutOfRangeException(nameof(minimumEffectiveSampleSize));

        var evaluation = corpus.Observations
            .Where(static row => row.Partition == CandidateCorpusPartition.Evaluation)
            .ToArray();
        if (evaluation.Length == 0)
            throw new InvalidOperationException("A análise de dependência exige partição Evaluation.");
        if (evaluation.Any(static row => row.Label == IndependentMatchLabel.Inconclusive))
            throw new InvalidOperationException(
                "Rótulos inconclusivos não podem entrar no diagnóstico de dependência sem metodologia específica.");
        if (!evaluation.Any(static row => row.Label == IndependentMatchLabel.Match) ||
            !evaluation.Any(static row => row.Label == IndependentMatchLabel.NonMatch))
            throw new InvalidOperationException("A partição Evaluation deve conter Match e NonMatch.");
        if (evaluation.Any(static row => row.Sample.DesignWeight <= 0m))
            throw new InvalidOperationException("Todos os pesos de desenho devem ser positivos.");

        var metrics = new List<CandidateEvidenceDependencyMetric>();
        foreach (var label in new[] { IndependentMatchLabel.Match, IndependentMatchLabel.NonMatch })
        {
            var rows = evaluation.Where(row => row.Label == label).ToArray();
            for (var leftIndex = 0; leftIndex < Features.Length; leftIndex++)
            for (var rightIndex = leftIndex + 1; rightIndex < Features.Length; rightIndex++)
            {
                foreach (var scope in Enum.GetValues<CandidateEvidenceDependencyScope>())
                    metrics.Add(AnalyzePair(
                        label,
                        scope,
                        Features[leftIndex],
                        Features[rightIndex],
                        rows,
                        minimumIndependentGroups,
                        minimumEffectiveSampleSize));
            }
        }

        var ordered = metrics
            .OrderBy(static metric => metric.Label)
            .ThenBy(static metric => metric.LeftFeature, StringComparer.Ordinal)
            .ThenBy(static metric => metric.RightFeature, StringComparer.Ordinal)
            .ThenBy(static metric => metric.Scope)
            .ToArray();
        var fingerprint = Fingerprint(corpus, evaluation.Length, ordered);

        return new CandidateEvidenceDependencyReport(
            Version,
            corpus.Capture.FrameFingerprint,
            corpus.Capture.SelectionFingerprint,
            corpus.Manifest.Reference,
            CandidateLabeling.FeatureVersion,
            evaluation.Length,
            Array.AsReadOnly(ordered),
            fingerprint);
    }

    private static CandidateEvidenceDependencyMetric AnalyzePair(
        IndependentMatchLabel label,
        CandidateEvidenceDependencyScope scope,
        string leftFeature,
        string rightFeature,
        IReadOnlyList<ValidatedCandidateObservation> sourceRows,
        int minimumIndependentGroups,
        decimal minimumEffectiveSampleSize)
    {
        var rows = sourceRows
            .Select(row => new DependencyRow(
                row,
                State(row.Comparison, leftFeature),
                State(row.Comparison, rightFeature)))
            .Where(row => scope == CandidateEvidenceDependencyScope.AllStates ||
                (row.LeftState != "MISSING" && row.RightState != "MISSING"))
            .ToArray();

        var observedWeight = rows.Sum(static row => row.Observation.Sample.DesignWeight);
        var squareWeight = rows.Sum(static row =>
            row.Observation.Sample.DesignWeight * row.Observation.Sample.DesignWeight);
        var effectiveSampleSize = observedWeight > 0m && squareWeight > 0m
            ? observedWeight * observedWeight / squareWeight
            : 0m;
        var groups = rows.Select(static row => row.Observation.IndependenceGroupId).Distinct().Count();

        string? nonEstimableReason = null;
        if (rows.Length == 0 || observedWeight <= 0m)
            nonEstimableReason = "NO_OBSERVED_PAIRS";
        else if (groups < minimumIndependentGroups)
            nonEstimableReason = "INSUFFICIENT_INDEPENDENT_GROUPS";
        else if (effectiveSampleSize < minimumEffectiveSampleSize)
            nonEstimableReason = "INSUFFICIENT_EFFECTIVE_SAMPLE_SIZE";

        if (nonEstimableReason is not null)
        {
            return new CandidateEvidenceDependencyMetric(
                label,
                scope,
                leftFeature,
                rightFeature,
                rows.Length,
                groups,
                observedWeight,
                effectiveSampleSize,
                false,
                nonEstimableReason,
                null,
                null);
        }

        var totalWeight = (double)observedWeight;
        var leftWeights = new Dictionary<string, double>(StringComparer.Ordinal);
        var rightWeights = new Dictionary<string, double>(StringComparer.Ordinal);
        var jointWeights = new Dictionary<(string Left, string Right), double>();
        foreach (var row in rows)
        {
            var weight = (double)row.Observation.Sample.DesignWeight;
            leftWeights[row.LeftState] = leftWeights.GetValueOrDefault(row.LeftState) + weight;
            rightWeights[row.RightState] = rightWeights.GetValueOrDefault(row.RightState) + weight;
            var key = (row.LeftState, row.RightState);
            jointWeights[key] = jointWeights.GetValueOrDefault(key) + weight;
        }

        var leftStates = leftWeights.Keys.OrderBy(static state => state, StringComparer.Ordinal).ToArray();
        var rightStates = rightWeights.Keys.OrderBy(static state => state, StringComparer.Ordinal).ToArray();
        var totalVariation = 0d;
        var mutualInformation = 0d;
        foreach (var left in leftStates)
        foreach (var right in rightStates)
        {
            var leftProbability = leftWeights[left] / totalWeight;
            var rightProbability = rightWeights[right] / totalWeight;
            var jointProbability = jointWeights.GetValueOrDefault((left, right)) / totalWeight;
            var independentProbability = leftProbability * rightProbability;
            totalVariation += Math.Abs(jointProbability - independentProbability);
            if (jointProbability > 0d && independentProbability > 0d)
                mutualInformation += jointProbability * Math.Log(jointProbability / independentProbability);
        }
        totalVariation *= 0.5d;

        var leftEntropy = Entropy(leftWeights.Values, totalWeight);
        var rightEntropy = Entropy(rightWeights.Values, totalWeight);
        double? normalizedMutualInformation = null;
        var entropyDenominator = Math.Sqrt(leftEntropy * rightEntropy);
        if (entropyDenominator > 0d)
        {
            normalizedMutualInformation = Clamp01(mutualInformation / entropyDenominator);
        }

        return new CandidateEvidenceDependencyMetric(
            label,
            scope,
            leftFeature,
            rightFeature,
            rows.Length,
            groups,
            observedWeight,
            effectiveSampleSize,
            true,
            null,
            Clamp01(totalVariation),
            normalizedMutualInformation);
    }

    private static double Entropy(IEnumerable<double> weights, double totalWeight)
    {
        var entropy = 0d;
        foreach (var weight in weights)
        {
            var probability = weight / totalWeight;
            if (probability > 0d)
                entropy -= probability * Math.Log(probability);
        }
        return entropy;
    }

    private static double Clamp01(double value)
    {
        if (value < 0d && value > -1e-12d)
            return 0d;
        if (value > 1d && value < 1d + 1e-12d)
            return 1d;
        return Math.Clamp(value, 0d, 1d);
    }

    private static string State(CandidateComparisonVector comparison, string feature) => feature switch
    {
        "NOME" => comparison.Name?.ToString() ?? "MISSING",
        "NOME_MAE" => comparison.Mother?.ToString() ?? "MISSING",
        "NASCIMENTO_CONJUNTO" => comparison.BirthAgreementMask is { } birth
            ? BirthStates[birth]
            : "MISSING",
        _ => throw new InvalidOperationException($"Feature desconhecida: {feature}.")
    };

    private static string Fingerprint(
        ValidatedCandidateCorpus corpus,
        int evaluationPairs,
        IReadOnlyList<CandidateEvidenceDependencyMetric> metrics)
    {
        var builder = new StringBuilder();
        Append(builder, Version);
        Append(builder, corpus.Capture.FrameFingerprint);
        Append(builder, corpus.Capture.SelectionFingerprint);
        Append(builder, corpus.Manifest.Reference);
        Append(builder, CandidateLabeling.FeatureVersion);
        Append(builder, evaluationPairs.ToString(CultureInfo.InvariantCulture));

        foreach (var metric in metrics)
        {
            Append(builder, metric.Label.ToString());
            Append(builder, metric.Scope.ToString());
            Append(builder, metric.LeftFeature);
            Append(builder, metric.RightFeature);
            Append(builder, metric.PairCount.ToString(CultureInfo.InvariantCulture));
            Append(builder, metric.IndependentGroups.ToString(CultureInfo.InvariantCulture));
            Append(builder, metric.ObservedWeight.ToString("G29", CultureInfo.InvariantCulture));
            Append(builder, metric.EffectiveSampleSize.ToString("G29", CultureInfo.InvariantCulture));
            Append(builder, metric.IsEstimable ? "1" : "0");
            Append(builder, metric.NonEstimableReason ?? string.Empty);
            Append(builder, Double(metric.TotalVariationDistance));
            Append(builder, Double(metric.NormalizedMutualInformation));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append('\n');

    private static string Double(double? value) =>
        value is null ? "-" : value.Value.ToString("R", CultureInfo.InvariantCulture);

    private sealed record DependencyRow(
        ValidatedCandidateObservation Observation,
        string LeftState,
        string RightState);
}
