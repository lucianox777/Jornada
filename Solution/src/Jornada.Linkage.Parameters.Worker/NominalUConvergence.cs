namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Decide a fonte operacional de u nominal sem misturar universos silenciosamente.
///
/// O scorer atual recebe a união deduplicada dos passes e não carrega o passe de origem
/// até a decisão. Portanto o estimador operacional é u condicionado à união do ruleset.
/// As distribuições por passe são medidas como diagnóstico e como requisito de suficiência:
/// nenhum passe pode ficar estatisticamente invisível quando o bootstrap IBGE é retirado.
/// </summary>
public sealed record NominalUConvergenceOptions(
    int MinimumConditionedPairs = 5_000,
    int MinimumConditionedPairsPerPass = 1_000)
{
    public void Validate()
    {
        if (MinimumConditionedPairs <= 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumConditionedPairs));
        if (MinimumConditionedPairsPerPass <= 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumConditionedPairsPerPass));
        if (MinimumConditionedPairsPerPass > MinimumConditionedPairs)
            throw new ArgumentException(
                "MinimumConditionedPairsPerPass não pode exceder MinimumConditionedPairs.");
    }
}

public static class NominalUConvergence
{
    public const string MethodVersion = "BLOCKING_CONDITIONED_NOMINAL_U_WITH_IBGE_BOOTSTRAP_V1";

    public static IReadOnlyDictionary<string, decimal> Apply(
        IReadOnlyDictionary<string, decimal> estimatedParameters,
        IbgeNominalUReferenceInfo reference,
        IbgeNominalUBootstrapEstimate personNameBootstrap,
        IbgeNominalUBootstrapEstimate motherNameBootstrap,
        IReadOnlyList<BlockingPassNominalUSupport> passSupport,
        NominalUConvergenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(estimatedParameters);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(personNameBootstrap);
        ArgumentNullException.ThrowIfNull(motherNameBootstrap);
        ArgumentNullException.ThrowIfNull(passSupport);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (passSupport.Count == 0)
            throw new InvalidOperationException("Convergência de u nominal exige suporte por passe do ruleset efetivo.");

        var result = new Dictionary<string, decimal>(estimatedParameters, StringComparer.Ordinal);

        foreach (var state in personNameBootstrap.States)
        {
            var suffix = state.State;
            if (result.TryGetValue($"SUPPORT_U_NOME_{suffix}", out var blockingSupport))
                result[$"BLOCKING_SUPPORT_U_NOME_{suffix}"] = blockingSupport;
            result[$"IBGE_MC_SUPPORT_U_NOME_{suffix}"] = state.Support;
        }

        foreach (var state in motherNameBootstrap.States)
        {
            var suffix = state.State;
            if (result.TryGetValue($"SUPPORT_U_NOME_MAE_{suffix}", out var blockingSupport))
                result[$"BLOCKING_SUPPORT_U_NOME_MAE_{suffix}"] = blockingSupport;
            result[$"IBGE_MC_SUPPORT_U_NOME_MAE_{suffix}"] = state.Support;
        }
        if (result.TryGetValue("SUPPORT_U_NOME_MAE_MISSING", out var missingSupport))
            result["BLOCKING_SUPPORT_U_NOME_MAE_MISSING"] = missingSupport;

        var conditionedNamePairs = SumSupport(result, "SUPPORT_U_NOME_", LinkageParameterCatalog.NameStates);
        var conditionedMotherPresentPairs = SumSupport(
            result,
            "SUPPORT_U_NOME_MAE_",
            LinkageParameterCatalog.NameStates);

        var allPassesNameSufficient = passSupport.All(
            pass => pass.SampleSize >= options.MinimumConditionedPairsPerPass);
        var allPassesMotherSufficient = passSupport.All(
            pass => PresentMotherSupport(pass) >= options.MinimumConditionedPairsPerPass);

        var useConditionedName =
            conditionedNamePairs >= options.MinimumConditionedPairs &&
            allPassesNameSufficient;
        var useConditionedMother =
            conditionedMotherPresentPairs >= options.MinimumConditionedPairs &&
            allPassesMotherSufficient;

        if (!useConditionedName)
            ApplyPersonBootstrap(result, personNameBootstrap);

        if (!useConditionedMother)
            ApplyMotherBootstrap(result, motherNameBootstrap);

        result["NOMINAL_U_CONVERGENCE_V1"] = 1m;
        result["NOMINAL_U_MIN_CONDITIONED_PAIRS"] = options.MinimumConditionedPairs;
        result["NOMINAL_U_MIN_CONDITIONED_PAIRS_PER_PASS"] = options.MinimumConditionedPairsPerPass;
        result["NOMINAL_U_NOME_CONDITIONED_PAIR_COUNT"] = conditionedNamePairs;
        result["NOMINAL_U_NOME_MAE_PRESENT_CONDITIONED_PAIR_COUNT"] = conditionedMotherPresentPairs;
        result["NOMINAL_U_ALL_PASSES_NAME_SUFFICIENT"] = allPassesNameSufficient ? 1m : 0m;
        result["NOMINAL_U_ALL_PASSES_MOTHER_SUFFICIENT"] = allPassesMotherSufficient ? 1m : 0m;
        result["NOMINAL_U_NOME_SOURCE_BLOCKING_CONDITIONED"] = useConditionedName ? 1m : 0m;
        result["NOMINAL_U_NOME_MAE_SOURCE_BLOCKING_CONDITIONED"] = useConditionedMother ? 1m : 0m;
        result["IBGE_MC_NOMINAL_U_BOOTSTRAP_AVAILABLE"] = 1m;
        result["IBGE_MC_NOMINAL_U_APPLIED_NOME"] = useConditionedName ? 0m : 1m;
        result["IBGE_MC_NOMINAL_U_APPLIED_NOME_MAE"] = useConditionedMother ? 0m : 1m;
        result["IBGE_MC_NOMINAL_U_PAIR_COUNT"] = personNameBootstrap.PairCount;
        result["IBGE_MC_NOMINAL_U_SEED_PERSON"] = personNameBootstrap.Seed;
        result["IBGE_MC_NOMINAL_U_SEED_MOTHER"] = motherNameBootstrap.Seed;
        result["IBGE_NAME_REFERENCE_ID"] = reference.Id;
        result["IBGE_MC_PERSON_EXACT_ANALYTIC"] = personNameBootstrap.AnalyticExactSyntheticFullNameProbability;
        result["IBGE_MC_MOTHER_EXACT_ANALYTIC"] = motherNameBootstrap.AnalyticExactSyntheticFullNameProbability;

        for (var index = 0; index < passSupport.Count; index++)
        {
            var pass = passSupport[index];
            var prefix = $"BLOCKING_PASS_U_{index + 1:D2}";
            result[$"{prefix}_SAMPLE_SIZE"] = pass.SampleSize;
            foreach (var state in LinkageParameterCatalog.NameStates)
            {
                result[$"{prefix}_NOME_{state}"] =
                    pass.NameStateSupport.TryGetValue(state, out var count) ? count : 0;
                result[$"{prefix}_NOME_MAE_{state}"] =
                    pass.MotherNameStateSupport.TryGetValue(state, out var motherCount) ? motherCount : 0;
            }
            result[$"{prefix}_NOME_MAE_MISSING"] =
                pass.MotherNameStateSupport.TryGetValue("MISSING", out var missingCount) ? missingCount : 0;
        }
        result["BLOCKING_PASS_U_COUNT"] = passSupport.Count;

        foreach (var state in LinkageParameterCatalog.NameStates)
        {
            if (!result.TryGetValue($"IBGE_MC_SUPPORT_U_NOME_{state}", out var personSupport) || personSupport <= 0m)
                throw new InvalidOperationException(
                    $"Monte Carlo IBGE sem suporte para U_NOME_{state}; aumente LinkageParameters:IbgeNominalU:PairCount.");

            if (!result.TryGetValue($"IBGE_MC_SUPPORT_U_NOME_MAE_{state}", out var motherSupport) || motherSupport <= 0m)
                throw new InvalidOperationException(
                    $"Monte Carlo IBGE sem suporte para U_NOME_MAE_{state}; aumente LinkageParameters:IbgeNominalU:PairCount.");
        }

        return result;
    }

    private static void ApplyPersonBootstrap(
        IDictionary<string, decimal> result,
        IbgeNominalUBootstrapEstimate bootstrap)
    {
        foreach (var state in bootstrap.States)
            result[$"U_NOME_{state.State}"] = state.Probability;
    }

    private static void ApplyMotherBootstrap(
        IDictionary<string, decimal> result,
        IbgeNominalUBootstrapEstimate bootstrap)
    {
        var missingProbability = result.TryGetValue("U_NOME_MAE_MISSING", out var missing)
            ? missing
            : 0m;
        var presentMass = 1m - missingProbability;
        if (presentMass <= 0m)
            throw new InvalidOperationException(
                "Nome da mãe está ausente em 100% da amostra u condicionada; não há massa observável para bootstrap nominal.");

        foreach (var state in bootstrap.States)
            result[$"U_NOME_MAE_{state.State}"] = presentMass * state.Probability;
    }

    private static long PresentMotherSupport(BlockingPassNominalUSupport pass) =>
        LinkageParameterCatalog.NameStates.Sum(
            state => pass.MotherNameStateSupport.TryGetValue(state, out var count) ? count : 0L);

    private static long SumSupport(
        IReadOnlyDictionary<string, decimal> parameters,
        string prefix,
        IReadOnlyList<string> states)
    {
        decimal total = 0m;
        foreach (var state in states)
        {
            if (parameters.TryGetValue(prefix + state, out var value))
                total += value;
        }

        return decimal.ToInt64(total);
    }
}
