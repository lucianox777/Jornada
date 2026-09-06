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
        DateOnly? rightBirthDate = null)
    {
        var prior = blockCandidateCount is > 0
            ? CalculateBlockPrior(parameters, blockCandidateCount.Value)
            : Get(parameters, "PRIOR_MATCH_PROBABILITY");
        var logOdds = Logit((double)prior);
        logOdds += LogLikelihoodRatio(parameters, "NOME", nameState);
        logOdds += LogLikelihoodRatio(parameters, "NOME_MAE", motherNameState);

        // V2: nascimento deixa de ser uma evidência indivisível. Dia, mês e ano
        // contribuem separadamente quando o modelo possui os parâmetros calibrados.
        // Modelos V1 continuam válidos: se os parâmetros V2 não existirem, a
        // contribuição dos componentes é neutra.
        if (leftBirthDate is { } left && rightBirthDate is { } right)
        {
            logOdds += TryBinaryLikelihoodRatio(parameters, "NASC_DIA", left.Day == right.Day);
            logOdds += TryBinaryLikelihoodRatio(parameters, "NASC_MES", left.Month == right.Month);
            logOdds += TryBinaryLikelihoodRatio(parameters, "NASC_ANO", left.Year == right.Year);
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
        NameComparisonState state)
    {
        var suffix = state.ToString();
        var m = ClampProbability(Get(parameters, $"M_{attribute}_{suffix}"));
        var u = ClampProbability(Get(parameters, $"U_{attribute}_{suffix}"));
        return Math.Log((double)m / (double)u);
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
