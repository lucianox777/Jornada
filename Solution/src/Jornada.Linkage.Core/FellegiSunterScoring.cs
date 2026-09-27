using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

public readonly record struct FellegiSunterScore(decimal Posterior, decimal LogOdds);

// Somente a fronteira contratual usa decimal; a matemática usa float64.
internal readonly record struct FellegiSunterRawScore(double Posterior, double LogOdds);

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
        NameComparisonState? nameState,
        NameComparisonState? motherNameState,
        int? blockCandidateCount = null,
        DateOnly? leftBirthDate = null,
        DateOnly? rightBirthDate = null) =>
        Calculate(parameters, nameState, motherNameState, blockCandidateCount, leftBirthDate, rightBirthDate).Posterior;

    public static FellegiSunterScore Calculate(
        IReadOnlyDictionary<string, decimal> parameters,
        NameComparisonState? nameState,
        NameComparisonState? motherNameState,
        int? blockCandidateCount = null,
        DateOnly? leftBirthDate = null,
        DateOnly? rightBirthDate = null) =>
        ToContractScore(CalculateRaw(ToDoubleParameters(parameters), nameState, motherNameState,
            blockCandidateCount, leftBirthDate, rightBirthDate));

    // Materializa o snapshot numérico uma única vez na carga do modelo.
    internal static IReadOnlyDictionary<string, double> ToDoubleParameters(
        IReadOnlyDictionary<string, decimal> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return parameters.ToDictionary(static item => item.Key,
            static item => (double)item.Value, StringComparer.Ordinal);
    }

    // Mantém a semântica legada do arredondamento exclusivamente na fronteira SQL/contrato.
    internal static FellegiSunterScore ToContractScore(FellegiSunterRawScore raw)
    {
        if (!double.IsFinite(raw.Posterior) || !double.IsFinite(raw.LogOdds))
            throw new InvalidOperationException("Score probabilístico não finito.");
        return new FellegiSunterScore(
            Math.Round((decimal)raw.Posterior, 8, MidpointRounding.AwayFromZero),
            Math.Round((decimal)raw.LogOdds, 8, MidpointRounding.AwayFromZero));
    }

    // Hot path: não materializa breakdown nem faz cast decimal de cada contribuição.
    internal static FellegiSunterRawScore CalculateRaw(
        IReadOnlyDictionary<string, double> parameters,
        NameComparisonState? nameState,
        NameComparisonState? motherNameState,
        int? blockCandidateCount = null,
        DateOnly? leftBirthDate = null,
        DateOnly? rightBirthDate = null) =>
        Evaluate(parameters, nameState, motherNameState, blockCandidateCount,
            leftBirthDate, rightBirthDate, captureBreakdown: false).Score;

    /// <summary>
    /// Mesmo núcleo double do runtime; as conversões para decimal são apenas diagnósticas.
    /// </summary>
    public static FellegiSunterScoreBreakdown CalculateWithBreakdown(
        IReadOnlyDictionary<string, decimal> parameters,
        NameComparisonState? nameState,
        NameComparisonState? motherNameState,
        int? blockCandidateCount = null,
        DateOnly? leftBirthDate = null,
        DateOnly? rightBirthDate = null)
    {
        var result = Evaluate(ToDoubleParameters(parameters), nameState, motherNameState,
            blockCandidateCount, leftBirthDate, rightBirthDate, captureBreakdown: true);
        var contributions = result.Contributions!
            .Select(static item => new FellegiSunterEvidenceContribution(
                item.Evidence,
                item.State,
                item.M is { } m ? (decimal)m : null,
                item.U is { } u ? (decimal)u : null,
                (decimal)item.LogLikelihoodRatio))
            .ToArray();
        return new FellegiSunterScoreBreakdown(
            ToContractScore(result.Score),
            result.UsesBlockPrior ? "BLOCK_CANDIDATE_COUNT" : "MODEL_PRIOR",
            (decimal)result.Prior,
            (decimal)result.PriorLogOdds,
            contributions);
    }

    private readonly record struct RawContribution(
        string Evidence, string State, double? M, double? U, double LogLikelihoodRatio);

    private readonly record struct RawEvaluation(
        FellegiSunterRawScore Score,
        bool UsesBlockPrior,
        double Prior,
        double PriorLogOdds,
        List<RawContribution>? Contributions);

    private static RawEvaluation Evaluate(
        IReadOnlyDictionary<string, double> parameters,
        NameComparisonState? nameState,
        NameComparisonState? motherNameState,
        int? blockCandidateCount,
        DateOnly? leftBirthDate,
        DateOnly? rightBirthDate,
        bool captureBreakdown)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var decisionV6 = parameters.TryGetValue(LinkageParameterCatalog.DecisionEvidenceScoring, out var decisionFlag) && decisionFlag >= 1d;
        var usesBlockPrior = !decisionV6 && blockCandidateCount is > 0;
        var prior = usesBlockPrior
            ? CalculateBlockPrior(parameters, blockCandidateCount!.Value)
            : Get(parameters, LinkageParameterCatalog.PriorMatchProbability);
        var priorLogOdds = Logit(prior);
        var logOdds = priorLogOdds;
        var contributions = captureBreakdown ? new List<RawContribution>(6) : null;

        void Add(string evidence, string state, (double? M, double? U, double LogLikelihoodRatio) value)
        {
            logOdds += value.LogLikelihoodRatio;
            contributions?.Add(new RawContribution(
                evidence,
                state,
                value.M,
                value.U,
                value.LogLikelihoodRatio));
        }

        if (nameState is { } observedNameState)
        {
            Add("NOME", observedNameState.ToString(),
                RequiredLikelihoodRatio(parameters, "NOME", observedNameState.ToString()));
        }
        else
        {
            // Não existe distribuição calibrada NOME_MISSING no modelo corrente.
            // Até sua calibração explícita, ausência é evidência indisponível/neutra.
            Add("NOME", "MISSING_NEUTRAL", (null, null, 0d));
        }

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
            if (parameters.TryGetValue(LinkageParameterCatalog.BirthSemanticEvidenceScoring, out var semanticBirth) && semanticBirth >= 1d)
            {
                var state = BirthDateSemanticEvidence.Classify(left, right);
                Add("NASCIMENTO_SEMANTICO", state,
                    RequiredLikelihoodRatio(parameters, "NASCIMENTO_SEMANTICO", state));
            }
            else if (parameters.TryGetValue(LinkageParameterCatalog.BirthJointEvidenceScoring, out var jointBirth) && jointBirth >= 1d)
            {
                var mask = (left.Day == right.Day ? 1 : 0) | (left.Month == right.Month ? 2 : 0) | (left.Year == right.Year ? 4 : 0);
                var state = LinkageParameterCatalog.BirthJointStates[mask];
                Add("NASCIMENTO_CONJUNTO", state,
                    RequiredLikelihoodRatio(parameters, "NASCIMENTO_CONJUNTO", state));
            }
            else if (parameters.TryGetValue(LinkageParameterCatalog.BirthSingleEvidenceScoring, out var singleBirth) && singleBirth >= 1d)
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
        var score = new FellegiSunterRawScore(posterior, logOdds);

        return new RawEvaluation(score, usesBlockPrior, prior, priorLogOdds, contributions);

        void AddBinary(string attribute, bool exact)
        {
            var state = exact ? "EXACT" : "DIFF";
            Add(attribute, state, OptionalLikelihoodRatio(parameters, attribute, state));
        }
    }

    private static double CalculateBlockPrior(IReadOnlyDictionary<string, double> parameters, int candidateCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(candidateCount);
        var min = parameters.TryGetValue(LinkageParameterCatalog.PriorBlockMin, out var pmin) ? pmin : 0.000001d;
        var max = parameters.TryGetValue(LinkageParameterCatalog.PriorBlockMax, out var pmax) ? pmax : 0.25d;
        return Math.Clamp(1d / candidateCount, min, max);
    }

    private static (double? M, double? U, double LogLikelihoodRatio) RequiredLikelihoodRatio(
        IReadOnlyDictionary<string, double> parameters,
        string attribute,
        string suffix)
    {
        var m = ClampProbability(Get(parameters, $"M_{attribute}_{suffix}"));
        var u = ClampProbability(Get(parameters, $"U_{attribute}_{suffix}"));
        return (m, u, Math.Log(m / u));
    }

    private static (double? M, double? U, double LogLikelihoodRatio) OptionalLikelihoodRatio(
        IReadOnlyDictionary<string, double> parameters,
        string attribute,
        string suffix)
    {
        if (!parameters.TryGetValue($"M_{attribute}_{suffix}", out var rawM) ||
            !parameters.TryGetValue($"U_{attribute}_{suffix}", out var rawU))
            return (null, null, 0d);

        var m = ClampProbability(rawM);
        var u = ClampProbability(rawU);
        return (m, u, Math.Log(m / u));
    }

    private static double Get(IReadOnlyDictionary<string, double> parameters, string name) =>
        parameters.TryGetValue(name, out var value) ? value : throw new InvalidOperationException($"Parâmetro de linkage ausente: {name}");

    private static double ClampProbability(decimal value) => Math.Clamp(value, 0.000000001d, 0.999999999d);

    private static double Logit(double probability)
    {
        var p = Math.Clamp(probability, 0.0000001d, 0.9999999d);
        return Math.Log(p / (1d - p));
    }
}
