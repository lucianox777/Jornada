using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

public static class FellegiSunterScoring
{
    public static decimal CalculatePosterior(
        IReadOnlyDictionary<string, decimal> parameters,
        NameComparisonState nameState,
        NameComparisonState motherNameState,
        int? blockCandidateCount = null)
    {
        var prior = blockCandidateCount is > 0
            ? CalculateBlockPrior(parameters, blockCandidateCount.Value)
            : Get(parameters, "PRIOR_MATCH_PROBABILITY");
        var logOdds = Logit((double)prior);
        logOdds += LogLikelihoodRatio(parameters, "NOME", nameState);
        logOdds += LogLikelihoodRatio(parameters, "NOME_MAE", motherNameState);

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
