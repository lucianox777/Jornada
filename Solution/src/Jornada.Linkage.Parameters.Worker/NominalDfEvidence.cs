using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Evidência do primeiro estágio DF (distância/similaridade nominal + frequência).
/// Não produz decisão por si só: threshold e região resolvida pertencem ao Calibrador.
/// O score DF não é somado posteriormente ao Fellegi-Sunter; se DF não resolver,
/// o FS avalia o par com sua própria decomposição de evidências.
/// </summary>
public sealed record NominalDfEvidence(
    double Similarity,
    NameComparisonState ComparisonState,
    decimal? LeftFrequency,
    decimal? RightFrequency,
    decimal? EffectiveFrequency,
    double? TermFrequencyLogAdjustment,
    bool FrequencyCensored,
    string SimilarityAlgorithmVersion,
    string TermFrequencyAlgorithmVersion);

public static class NominalDfEvidenceCalculator
{
    public const string AlgorithmVersion = "NOMINAL_DF_SPLINK_COMPATIBLE_V1";

    public static NominalDfEvidence Evaluate(
        string? left,
        string? right,
        decimal? leftFrequency,
        decimal? rightFrequency,
        decimal? referenceUProbability = null,
        decimal tfWeight = 1m,
        decimal tfMinimumUValue = 0m,
        bool frequencyCensored = false)
    {
        var normalizedLeft = IdentityComparison.NormalizeText(left);
        var normalizedRight = IdentityComparison.NormalizeText(right);

        var similarity = normalizedLeft is null || normalizedRight is null
            ? 0d
            : IdentityComparison.JaroWinkler(normalizedLeft, normalizedRight);
        var state = IdentityComparison.CompareName(left, right);

        decimal? effectiveFrequency = null;
        double? adjustment = null;

        if (leftFrequency is { } lf && rightFrequency is { } rf)
        {
            effectiveFrequency = SplinkCompatibleTermFrequency.EffectiveFrequency(lf, rf, tfMinimumUValue);
            if (referenceUProbability is { } u)
            {
                adjustment = SplinkCompatibleTermFrequency.LogBayesAdjustment(
                    lf,
                    rf,
                    u,
                    tfWeight,
                    tfMinimumUValue);
            }
        }

        return new NominalDfEvidence(
            similarity,
            state,
            leftFrequency,
            rightFrequency,
            effectiveFrequency,
            adjustment,
            frequencyCensored,
            "JARO_WINKLER@V1",
            SplinkCompatibleTermFrequency.AlgorithmVersion);
    }
}
