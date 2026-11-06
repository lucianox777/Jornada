using System.Globalization;
using System.Text.Json.Serialization;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Declarative, finite MU search space. Does not invent values or execute a continuous search.
/// Historical plans without MuEstimationMethods keep their original semantics and hashes.
/// </summary>
public sealed record V9MuEstimationMethod(
    string Method,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    V9MuEstimationParameterGrid? Parameters = null);

public sealed record V9MuEstimationParameterGrid(
    IReadOnlyList<decimal> ClassMassCoverage,
    IReadOnlyList<long> MinimumObservedMPerCell,
    IReadOnlyList<long> MinimumObservedUPerCell,
    IReadOnlyList<bool> RetainSupportedTail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<decimal>? UBackoffJointMixtureWeights = null);

public sealed record V9MuEstimationVariant(string Method, string Key, V9JointSupportPolicy? Policy);

public static class V9MuEstimationSearchSpace
{
    public const string Default = "DEFAULT";
    public const string Classwise = "CLASSWISE_SUPPORT_BACKOFF";

    public static IReadOnlyList<V9MuEstimationVariant> Enumerate(IReadOnlyList<V9MuEstimationMethod> methods)
    {
        if (methods is null || methods.Count == 0)
            throw new InvalidDataException("MuEstimationMethods exige ao menos um método declarado.");
        if (methods.Any(x => x is null || string.IsNullOrWhiteSpace(x.Method))
            || methods.Select(x => x.Method).Distinct(StringComparer.Ordinal).Count() != methods.Count)
            throw new InvalidDataException("Métodos M/U ausentes ou duplicados.");
        var result = new List<V9MuEstimationVariant>();
        foreach (var method in methods)
        {
            if (method.Method == Default)
            {
                if (method.Parameters is not null)
                    throw new InvalidDataException("DEFAULT reproduz V2: não admite parâmetros de fallback.");
                result.Add(new(Default, Default, null));
                continue;
            }
            if (method.Method != Classwise)
                throw new InvalidDataException("Método M/U desconhecido: " + method.Method);
            var p = method.Parameters
                ?? throw new InvalidDataException("CLASSWISE_SUPPORT_BACKOFF exige Parameters explícito.");
            CheckValues(p.ClassMassCoverage, "ClassMassCoverage");
            CheckValues(p.MinimumObservedMPerCell, "MinimumObservedMPerCell");
            CheckValues(p.MinimumObservedUPerCell, "MinimumObservedUPerCell");
            CheckValues(p.RetainSupportedTail, "RetainSupportedTail");
            if (p.UBackoffJointMixtureWeights is { } declaredWeights)
            {
                CheckValues(declaredWeights, "UBackoffJointMixtureWeights");
                if (declaredWeights.Any(w => w is < 0m or > 1m))
                    throw new InvalidDataException("U rare mixture weights must be between 0 and 1.");
            }
            decimal?[] uWeights = p.UBackoffJointMixtureWeights?.Select(w => (decimal?)w).ToArray() ?? [null];
            if (p.ClassMassCoverage.Any(x => x is <= 0 or >= 1)
                || p.MinimumObservedMPerCell.Any(x => x < 1)
                || p.MinimumObservedUPerCell.Any(x => x < 1))
                throw new InvalidDataException("CLASSWISE exige cobertura (0,1) e suporte M/U positivo.");
            // All values are explicit finite alternatives. No hidden search or truncation.
            var count = checked((long)p.ClassMassCoverage.Count * p.MinimumObservedMPerCell.Count
                * p.MinimumObservedUPerCell.Count * p.RetainSupportedTail.Count * uWeights.Length);
            if (count > int.MaxValue)
                throw new InvalidDataException("Grade M/U supera capacidade de enumeração.");
            foreach (var coverage in p.ClassMassCoverage)
            foreach (var minM in p.MinimumObservedMPerCell)
            foreach (var minU in p.MinimumObservedUPerCell)
            foreach (var retain in p.RetainSupportedTail)
            foreach (var uWeight in uWeights)
            {
                var policy = new V9JointSupportPolicy(coverage, minM, minU,
                    retain ? V9JointSupportEstimation.SupportedTailRule : null,
                    uWeight);
                V9JointSupportEstimation.ValidatePolicy(policy);
                var key = Classwise + "_C" + coverage.ToString("G29", CultureInfo.InvariantCulture)
                    + "_M" + minM.ToString(CultureInfo.InvariantCulture)
                    + "_U" + minU.ToString(CultureInfo.InvariantCulture)
                    + "_T" + (retain ? "1" : "0")
                    + (uWeight is null ? "" : "_UJW" + uWeight.Value.ToString("G29", CultureInfo.InvariantCulture));
                result.Add(new(Classwise, key, policy));
            }
        }
        return result.AsReadOnly();
    }

    private static void CheckValues<T>(IReadOnlyList<T>? values, string name) where T : notnull
    {
        if (values is null || values.Count == 0 || values.Distinct().Count() != values.Count)
            throw new InvalidDataException("Valores M/U ausentes, vazios ou duplicados: " + name);
    }

    public static IReadOnlyList<V9FsAdaptiveOptionConfiguration> Expand(
        IReadOnlyList<V9FsAdaptiveOptionConfiguration> originalOptions,
        IReadOnlyList<V9MuEstimationMethod> methods,
        long maximumOptionCount)
    {
        if (originalOptions is null || originalOptions.Count == 0
            || originalOptions.Any(x => x is null || x.JointSupportPolicy is not null))
            throw new InvalidDataException("Grade M/U exige opções-base sem política conjunta concorrente.");
        var variants = Enumerate(methods);
        if (maximumOptionCount <= 0 || (long)originalOptions.Count * variants.Count > maximumOptionCount)
            throw new InvalidDataException("Grade M/U excede orçamento declarado; nenhuma opção foi omitida.");
        var expanded = originalOptions.SelectMany(option => variants.Select(variant => option with
        {
            OptionId = option.OptionId + "__MU_" + variant.Key,
            JointSupportPolicy = variant.Policy
        })).ToArray();
        if (expanded.Select(x => x.OptionId).Distinct(StringComparer.Ordinal).Count() != expanded.Length)
            throw new InvalidDataException("Grade M/U produziu identificadores duplicados.");
        return Array.AsReadOnly(expanded);
    }
}
