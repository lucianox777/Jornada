using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

public static class FellegiSunterScoring
{
    public static decimal CalculatePosterior(
        IReadOnlyDictionary<string, decimal> parameters,
        NameComparisonState nameState,
        NameComparisonState motherNameState,
        int? blockCandidateCount = null,
        DateOnly? leftBirthDate = null,
        DateOnly? rightBirthDate = null,
        NameFrequencyStratum nameFrequencyStratum = NameFrequencyStratum.UNKNOWN,
        NameFrequencyStratum motherNameFrequencyStratum = NameFrequencyStratum.UNKNOWN)
    {
        var prior = blockCandidateCount is > 0
            ? CalculateBlockPrior(parameters, blockCandidateCount.Value)
            : Get(parameters, "PRIOR_MATCH_PROBABILITY");
        var logOdds = Logit((double)prior);
        logOdds += LogLikelihoodRatio(parameters, "NOME", nameState, nameFrequencyStratum);
        logOdds += LogLikelihoodRatio(parameters, "NOME_MAE", motherNameState, motherNameFrequencyStratum);

        if (leftBirthDate is { } left && rightBirthDate is { } right)
        {
            // V3: nascimento é uma única variável probabilística. Isto evita tratar
            // dia/mês/ano como três observações independentes do mesmo evento de
            // transcrição. Modelos V2 históricos continuam reproduzíveis abaixo.
            if (parameters.TryGetValue("SCORING_BIRTH_SINGLE_EVIDENCE_V3", out var singleBirth) && singleBirth >= 1m)
            {
                logOdds += TryBinaryLikelihoodRatio(parameters, "DATA_NASCIMENTO", left == right);
            }
            else
            {
                // Compatibilidade de replay para modelos V2 já publicados. Novos modelos
                // não devem emitir SCORING_BIRTH_COMPONENTS_V2.
                logOdds += TryBinaryLikelihoodRatio(parameters, "NASC_DIA", left.Day == right.Day);
                logOdds += TryBinaryLikelihoodRatio(parameters, "NASC_MES", left.Month == right.Month);
                logOdds += TryBinaryLikelihoodRatio(parameters, "NASC_ANO", left.Year == right.Year);
            }
        }

        var posterior = 1d / (1d + Math.Exp(-Math.Clamp(logOdds, -40d, 40d)));
        return Math.Round((decimal)posterior, 8, MidpointRounding.AwayFromZero);
    }

    private static decimal CalculateBlockPrior(IReadOnlyDictionary<string, decimal> parameters, int candidateCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(candidateCount);
        var min = parameters.TryGetValue("PRIOR_BLOCK_MIN", out var pmin) ? pmin : 0.000001m;
        var max = parameters.TryGetValue("PRIOR_BLOCK_MAX", out var pmax) ? pmax : 0.25m;
        return Math.Clamp(1m / candidateCount, min, max);
    }

    private static double LogLikelihoodRatio(
        IReadOnlyDictionary<string, decimal> parameters,
        string attribute,
        NameComparisonState state,
        NameFrequencyStratum frequencyStratum)
    {
        var stateSuffix = state.ToString();
        var stratumSuffix = frequencyStratum.ToString();

        // Frequência é uma dimensão da calibração m/u, não um multiplicador da distância.
        // Modelos históricos e estrato UNKNOWN usam a distribuição marginal.
        var m = TryGetStratified(parameters, $"M_{attribute}_{stateSuffix}", stratumSuffix);
        var u = TryGetStratified(parameters, $"U_{attribute}_{stateSuffix}", stratumSuffix);
        return Math.Log((double)ClampProbability(m) / (double)ClampProbability(u));
    }

    private static decimal TryGetStratified(
        IReadOnlyDictionary<string, decimal> parameters,
        string baseName,
        string stratumSuffix)
    {
        if (!string.Equals(stratumSuffix, nameof(NameFrequencyStratum.UNKNOWN), StringComparison.Ordinal) &&
            parameters.TryGetValue($"{baseName}_{stratumSuffix}", out var stratified))
            return stratified;

        return Get(parameters, baseName);
    }

    private static double TryBinaryLikelihoodRatio(
        IReadOnlyDictionary<string, decimal> parameters,
        string attribute,
        bool exact)
    {
        var suffix = exact ? "EXACT" : "DIFF";
        if (!parameters.TryGetValue($"M_{attribute}_{suffix}", out var m) ||
            !parameters.TryGetValue($"U_{attribute}_{suffix}", out var u))
            return 0d;

        return Math.Log((double)ClampProbability(m) / (double)ClampProbability(u));
    }

    private static decimal Get(IReadOnlyDictionary<string, decimal> parameters, string name) =>
        parameters.TryGetValue(name, out var value)
            ? value
            : throw new InvalidOperationException($"Parâmetro de linkage ausente: {name}");

    private static decimal ClampProbability(decimal value) =>
        Math.Clamp(value, 0.000000001m, 0.999999999m);

    private static double Logit(double probability)
    {
        var p = Math.Clamp(probability, 0.0000001d, 0.9999999d);
        return Math.Log(p / (1d - p));
    }
}

/// <summary>
/// Estrato de frequência do atributo com a mesma semântica usada na comparação.
/// O estrato é uma covariável de calibração; não é um peso multiplicativo da distância.
/// </summary>
public enum NameFrequencyStratum
{
    UNKNOWN,
    RARE,
    UNCOMMON,
    COMMON,
    VERY_COMMON
}
