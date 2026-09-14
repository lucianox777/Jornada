using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

public static class FellegiSunterScoring
{
    public static decimal CalculatePosterior(
        IReadOnlyDictionary<string, decimal> parameters,
        NameComparisonState nameState,
        NameComparisonState? motherNameState,
        int? blockCandidateCount = null,
        DateOnly? leftBirthDate = null,
        DateOnly? rightBirthDate = null)
    {
        var prior = blockCandidateCount is > 0
            ? CalculateBlockPrior(parameters, blockCandidateCount.Value)
            : Get(parameters, LinkageParameterCatalog.PriorMatchProbability);
        var logOdds = Logit((double)prior);
        logOdds += LogLikelihoodRatio(parameters, "NOME", nameState);

        // Ausência de nome da mãe é evidência não observada: LR=1, log(LR)=0.
        // LOW continua reservado a uma comparação efetivamente observada de baixa similaridade.
        if (motherNameState is { } observedMotherNameState)
            logOdds += LogLikelihoodRatio(parameters, "NOME_MAE", observedMotherNameState);

        if (leftBirthDate is { } left && rightBirthDate is { } right)
        {
            // V3: data de nascimento é uma única evidência probabilística. A comparação inteira
            // contribui uma vez (EXACT/DIFF), evitando contar dia, mês e ano como evidências
            // independentes. V2 permanece somente para replay de modelos históricos.
            if (parameters.TryGetValue(LinkageParameterCatalog.BirthSingleEvidenceScoring, out var singleBirth)
                && singleBirth >= 1m)
            {
                logOdds += TryBinaryLikelihoodRatio(parameters, "DATA_NASCIMENTO", left == right);
            }
            else
            {
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
        var min = parameters.TryGetValue(LinkageParameterCatalog.PriorBlockMin, out var pmin) ? pmin : 0.000001m;
        var max = parameters.TryGetValue(LinkageParameterCatalog.PriorBlockMax, out var pmax) ? pmax : 0.25m;
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
