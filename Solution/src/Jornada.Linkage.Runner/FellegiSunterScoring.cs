using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

public readonly record struct FellegiSunterScore(decimal Posterior, decimal LogOdds);

public sealed record FellegiSunterEvidenceContribution(
    string Evidence,
    string State,
    decimal? MProbability,
    decimal? UProbability,
    decimal LogLikelihoodRatio);

public sealed record FellegiSunterScoreBreakdown(
    FellegiSunterScore Score,
    string PriorKind,
    decimal PriorProbability,
    decimal PriorLogOdds,
    IReadOnlyList<FellegiSunterEvidenceContribution> Contributions);

public static class FellegiSunterScoring
{
    public static decimal CalculatePosterior(
        IReadOnlyDictionary<string, decimal> parameters,
        NameComparisonState nameState,
        NameComparisonState? motherNameState,
        int? blockCandidateCount = null,
        DateOnly? leftBirthDate = null,
        DateOnly? rightBirthDate = null) =>
        Calculate(parameters, nameState, motherNameState, blockCandidateCount, leftBirthDate, rightBirthDate).Posterior;

    public static FellegiSunterScore Calculate(
        IReadOnlyDictionary<string, decimal> parameters,
        NameComparisonState nameState,
        NameComparisonState? motherNameState,
        int? blockCandidateCount = null,
        DateOnly? leftBirthDate = null,
        DateOnly? rightBirthDate = null) =>
        CalculateWithBreakdown(parameters, nameState, motherNameState, blockCandidateCount, leftBirthDate, rightBirthDate).Score;

    /// <summary>
    /// Calcula exatamente o mesmo score usado pelo runtime e expõe, sem alterar a decisão,
    /// a contribuição aditiva em log-odds de cada evidência. O breakdown é diagnóstico:
    /// nenhum valor é persistido nem reutilizado como nova evidência pelo scorer.
    /// </summary>
    public static FellegiSunterScoreBreakdown CalculateWithBreakdown(
        IReadOnlyDictionary<string, decimal> parameters,
        NameComparisonState nameState,
        NameComparisonState? motherNameState,
        int? blockCandidateCount = null,
        DateOnly? leftBirthDate = null,
        DateOnly? rightBirthDate = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var decisionV6 = parameters.TryGetValue(LinkageParameterCatalog.DecisionEvidenceScoring, out var decisionFlag) && decisionFlag >= 1m;
        var usesBlockPrior = !decisionV6 && blockCandidateCount is > 0;
        var prior = usesBlockPrior
            ? CalculateBlockPrior(parameters, blockCandidateCount!.Value)
            : Get(parameters, LinkageParameterCatalog.PriorMatchProbability);
        var priorLogOdds = Logit((double)prior);
        var logOdds = priorLogOdds;
        var contributions = new List<FellegiSunterEvidenceContribution>(6);

        void Add(string evidence, string state, (decimal? M, decimal? U, double LogLikelihoodRatio) value)
        {
            logOdds += value.LogLikelihoodRatio;
            contributions.Add(new FellegiSunterEvidenceContribution(
                evidence,
                state,
                value.M,
                value.U,
                (decimal)value.LogLikelihoodRatio));
        }

        Add("NOME", nameState.ToString(), RequiredLikelihoodRatio(parameters, "NOME", nameState.ToString()));

        if (motherNameState is { } observedMotherNameState)
        {
            Add("NOME_MAE", observedMotherNameState.ToString(),
                RequiredLikelihoodRatio(parameters, "NOME_MAE", observedMotherNameState.ToString()));
        }
        else if (decisionV6)
        {
            Add("NOME_MAE", "MISSING", RequiredLikelihoodRatio(parameters, "NOME_MAE", "MISSING"));
        }
        else
        {
            Add("NOME_MAE", "MISSING_NEUTRAL", (null, null, 0d));
        }

        if (leftBirthDate is { } left && rightBirthDate is { } right)
        {
            if (parameters.TryGetValue(LinkageParameterCatalog.BirthSemanticEvidenceScoring, out var semanticBirth) && semanticBirth >= 1m)
            {
                var state = BirthDateSemanticEvidence.Classify(left, right);
                Add("NASCIMENTO_SEMANTICO", state,
                    RequiredLikelihoodRatio(parameters, "NASCIMENTO_SEMANTICO", state));
            }
            else if (parameters.TryGetValue(LinkageParameterCatalog.BirthJointEvidenceScoring, out var jointBirth) && jointBirth >= 1m)
            {
                var mask = (left.Day == right.Day ? 1 : 0) | (left.Month == right.Month ? 2 : 0) | (left.Year == right.Year ? 4 : 0);
                var state = LinkageParameterCatalog.BirthJointStates[mask];
                Add("NASCIMENTO_CONJUNTO", state,
                    RequiredLikelihoodRatio(parameters, "NASCIMENTO_CONJUNTO", state));
            }
            else if (parameters.TryGetValue(LinkageParameterCatalog.BirthSingleEvidenceScoring, out var singleBirth) && singleBirth >= 1m)
            {
                var state = left == right ? "EXACT" : "DIFF";
                Add("DATA_NASCIMENTO", state, OptionalLikelihoodRatio(parameters, "DATA_NASCIMENTO", state));
            }
            else
            {
                AddBinary("NASC_DIA", left.Day == right.Day);
                AddBinary("NASC_MES", left.Month == right.Month);
                AddBinary("NASC_ANO", left.Year == right.Year);
            }
        }
        else
        {
            Add("NASCIMENTO", "MISSING_NEUTRAL", (null, null, 0d));
        }

        var posterior = 1d / (1d + Math.Exp(-Math.Clamp(logOdds, -40d, 40d)));
        var score = new FellegiSunterScore(
            Math.Round((decimal)posterior, 8, MidpointRounding.AwayFromZero),
            Math.Round((decimal)logOdds, 8, MidpointRounding.AwayFromZero));

        return new FellegiSunterScoreBreakdown(
            score,
            usesBlockPrior ? "BLOCK_CANDIDATE_COUNT" : "MODEL_PRIOR",
            prior,
            (decimal)priorLogOdds,
            contributions);

        void AddBinary(string attribute, bool exact)
        {
            var state = exact ? "EXACT" : "DIFF";
            Add(attribute, state, OptionalLikelihoodRatio(parameters, attribute, state));
        }
    }

    private static decimal CalculateBlockPrior(IReadOnlyDictionary<string, decimal> parameters, int candidateCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(candidateCount);
        var min = parameters.TryGetValue(LinkageParameterCatalog.PriorBlockMin, out var pmin) ? pmin : 0.000001m;
        var max = parameters.TryGetValue(LinkageParameterCatalog.PriorBlockMax, out var pmax) ? pmax : 0.25m;
        return Math.Clamp(1m / candidateCount, min, max);
    }

    private static (decimal? M, decimal? U, double LogLikelihoodRatio) RequiredLikelihoodRatio(
        IReadOnlyDictionary<string, decimal> parameters,
        string attribute,
        string suffix)
    {
        var m = ClampProbability(Get(parameters, $"M_{attribute}_{suffix}"));
        var u = ClampProbability(Get(parameters, $"U_{attribute}_{suffix}"));
        return (m, u, Math.Log((double)m / (double)u));
    }

    private static (decimal? M, decimal? U, double LogLikelihoodRatio) OptionalLikelihoodRatio(
        IReadOnlyDictionary<string, decimal> parameters,
        string attribute,
        string suffix)
    {
        if (!parameters.TryGetValue($"M_{attribute}_{suffix}", out var rawM) ||
            !parameters.TryGetValue($"U_{attribute}_{suffix}", out var rawU))
            return (null, null, 0d);

        var m = ClampProbability(rawM);
        var u = ClampProbability(rawU);
        return (m, u, Math.Log((double)m / (double)u));
    }

    private static decimal Get(IReadOnlyDictionary<string, decimal> parameters, string name) =>
        parameters.TryGetValue(name, out var value) ? value : throw new InvalidOperationException($"Parâmetro de linkage ausente: {name}");

    private static decimal ClampProbability(decimal value) => Math.Clamp(value, 0.000000001m, 0.999999999m);

    private static double Logit(double probability)
    {
        var p = Math.Clamp(probability, 0.0000001d, 0.9999999d);
        return Math.Log(p / (1d - p));
    }
}
