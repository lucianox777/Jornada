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
        if (minimumUValue < 0m || minimumUValue > 1m)
            throw new ArgumentOutOfRangeException(nameof(minimumUValue));

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
        if (weight < 0m)
            throw new ArgumentOutOfRangeException(nameof(weight));

        if (weight == 0m)
            return 0d;

        var effectiveFrequency = EffectiveFrequency(leftFrequency, rightFrequency, minimumUValue);
        var bayesFactor = (double)(referenceUProbability / effectiveFrequency);
        return (double)weight * Math.Log(bayesFactor);
    }

    private static void ValidateProbability(decimal value, string parameterName)
    {
        if (value <= 0m || value > 1m)
            throw new ArgumentOutOfRangeException(parameterName, "A frequência/probabilidade deve estar em (0, 1].");
    }
}
