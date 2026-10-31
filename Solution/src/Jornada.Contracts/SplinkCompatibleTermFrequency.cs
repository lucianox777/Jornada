namespace Jornada.Contracts;

/// <summary>
/// Implementação C# do ajuste de term frequency usado como referência pelo Splink.
///
/// O ajuste substitui, para o valor observado, a probabilidade u genérica de
/// concordância exata pela frequência do termo. Em comparação fuzzy são consideradas
/// as duas frequências e usada a maior, de forma conservadora. O piso opcional limita
/// a evidência produzida por termos extremamente raros.
///
/// A Jornada usa log natural porque o scorer Fellegi-Sunter corrente também usa log
/// natural. A base do log não altera ordenação/decisão desde que seja consistente.
/// </summary>
public static class SplinkCompatibleTermFrequency
{
    public const string AlgorithmVersion = "SPLINK_TERM_FREQUENCY_V1";

    public static decimal EffectiveFrequency(
        decimal leftFrequency,
        decimal rightFrequency,
        decimal minimumUValue = 0m)
    {
        ValidateProbability(leftFrequency, nameof(leftFrequency));
        ValidateProbability(rightFrequency, nameof(rightFrequency));
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumUValue, 0m);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumUValue, 1m);

        var frequency = Math.Max(leftFrequency, rightFrequency);
        return minimumUValue > 0m ? Math.Max(frequency, minimumUValue) : frequency;
    }

    /// <summary>
    /// Retorna a contribuição aditiva, em log-Bayes-factor, do ajuste de frequência.
    /// referenceUProbability é o u do nível exato usado como referência pela comparação.
    /// weight=0 desliga o ajuste; weight=1 aplica o ajuste integral.
    /// </summary>
    public static double LogBayesAdjustment(
        decimal leftFrequency,
        decimal rightFrequency,
        decimal referenceUProbability,
        decimal weight = 1m,
        decimal minimumUValue = 0m)
    {
        ValidateProbability(referenceUProbability, nameof(referenceUProbability));
        ArgumentOutOfRangeException.ThrowIfLessThan(weight, 0m);

        if (weight == 0m)
            return 0d;

        var effectiveFrequency = EffectiveFrequency(leftFrequency, rightFrequency, minimumUValue);
        var bayesFactor = (double)(referenceUProbability / effectiveFrequency);
        return (double)weight * Math.Log(bayesFactor);
    }

    /// <summary>
    /// V9: ajuste assimétrico conservador. Nunca bonifica termos raros.
    /// Preserva a rotina histórica de ajuste simétrico e seu resultado para peso zero.
    /// O chamador deve fornecer u da MESMA unidade nominal da frequência.
    /// </summary>
    public static double LogBayesAdjustmentAsymmetric(
        decimal leftFrequency,
        decimal rightFrequency,
        decimal referenceUProbability,
        decimal weight = 1m,
        decimal minimumUValue = 0m)
    {
        var adjustment = LogBayesAdjustment(
            leftFrequency, rightFrequency, referenceUProbability, weight, minimumUValue);
        return Math.Min(0d, adjustment);
    }

    /// <summary>
    /// Explicit V9 TF mode selector. Zero disables TF; symmetric preserves V8;
    /// asymmetric only penalizes common terms. No operational activation implied.
    /// </summary>
    public enum AdjustmentModeV9 { Zero, Symmetric, Asymmetric }

    public static double LogBayesAdjustmentV9(
        AdjustmentModeV9 mode,
        decimal leftFrequency,
        decimal rightFrequency,
        decimal referenceUProbability,
        decimal weight = 1m,
        decimal minimumUValue = 0m)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown V9 TF mode.");
        // Zero disables the adjustment, but invalid inputs must not be silently accepted.
        ValidateProbability(leftFrequency, nameof(leftFrequency));
        ValidateProbability(rightFrequency, nameof(rightFrequency));
        ValidateProbability(referenceUProbability, nameof(referenceUProbability));
        ArgumentOutOfRangeException.ThrowIfLessThan(weight, 0m);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumUValue, 0m);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumUValue, 1m);
        return mode switch
        {
            AdjustmentModeV9.Zero => 0d,
            AdjustmentModeV9.Symmetric => LogBayesAdjustment(
                leftFrequency, rightFrequency, referenceUProbability, weight, minimumUValue),
            AdjustmentModeV9.Asymmetric => LogBayesAdjustmentAsymmetric(
                leftFrequency, rightFrequency, referenceUProbability, weight, minimumUValue),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown V9 TF mode.")
        };
    }

    private static void ValidateProbability(decimal value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0m, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1m, parameterName);
    }
}
