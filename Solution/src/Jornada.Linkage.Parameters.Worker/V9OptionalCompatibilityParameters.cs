using System.Reflection;
using System.Text.Json;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// J/#1337 optional parameters never rewrite a historical plan. Unknown JSON
/// members are rejected by V9NestedCalibrationCli's strict serializer.
/// Existing scientific selection/timing/P95 gates are not changed by this
/// pure compatibility and provenance component.
/// </summary>
public static class V9OptionalCompatibilityParameters
{
    public const string FromPlan = "FROM_PLAN";
    public const string CodeDefault = "CODE_DEFAULT";
    public static readonly string[] DefaultTieBreakOrder = ["recall", "FDR upper", "SHA"];

    public sealed record Origin(string Source, JsonElement EffectiveValue);
    public sealed record OptionReport(
        string? OptionId,
        string? BaseOptionId,
        string? MuEstimationMethod,
        IReadOnlyDictionary<string, Origin> BaseOptionParameters,
        IReadOnlyDictionary<string, Origin> MuMethodParameters,
        IReadOnlyDictionary<string, Origin> NeutralParameters);

    public static void Validate(V9NestedCalibrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.RecallEquivalenceMargin is < 0m or > 1m)
            throw new InvalidDataException("RecallEquivalenceMargin fora de [0,1].");
        if (plan.RecallEquivalenceMargin is > 0m)
            throw new InvalidDataException(
                "RecallEquivalenceMargin não neutra exigiria alteração de seleção; não implementar D2 nem ignorar parâmetro.");
        if (plan.TieBreakOrder is { } declared
            && !declared.SequenceEqual(DefaultTieBreakOrder, StringComparer.Ordinal))
            throw new InvalidDataException(
                "TieBreakOrder diferente da ordem histórica ainda não tem implementador; não ignorar declaração.");
        if (plan.ClusterUnit is not null and not "IDENTITY")
            throw new InvalidDataException("ClusterUnit não suportada; agrupamento atual é IDENTITY.");
        if (plan.Estimator is not null and not "LABELED")
            throw new InvalidDataException("Estimator não suportado; estimador atual é LABELED.");
        if (plan.ManualReviewSampleSize is < 0)
            throw new InvalidDataException("ManualReviewSampleSize negativa.");
        if (plan.ManualReviewSampleSize is > 0)
            throw new InvalidDataException("ManualReviewSampleSize não neutra requer implementação; não ignorar plano.");
        if (plan.RecalibrationTriggers is true)
            throw new InvalidDataException("RecalibrationTriggers automático não autorizado pelo PO.");
    }

    /// <summary>Reject explicit null; missing is allowed and uses the C# default.</summary>
    public static void ValidateRawInput(byte[] planBytes)
    {
        using var doc = JsonDocument.Parse(planBytes);
        foreach (var field in new[] { "RecallEquivalenceMargin", "TieBreakOrder",
                     "ClusterUnit", "Estimator", "ManualReviewSampleSize",
                     "RecalibrationTriggers" })
        {
            if (doc.RootElement.TryGetProperty(field, out var value)
                && value.ValueKind == JsonValueKind.Null)
                throw new InvalidDataException(field + " explicitamente null; omita para usar default.");
        }
    }

    private static Origin Tag(object? value, bool present) =>
        new(present ? FromPlan : CodeDefault, JsonSerializer.SerializeToElement(value));

    private static IReadOnlyDictionary<string, Origin> Neutral(JsonElement raw, V9NestedCalibrationPlan plan)
    {
        bool Has(string name) => raw.TryGetProperty(name, out _);
        return new SortedDictionary<string, Origin>(StringComparer.Ordinal)
        {
            ["RecallEquivalenceMargin"] = Tag(plan.RecallEquivalenceMargin ?? 0m, Has("RecallEquivalenceMargin")),
            ["TieBreakOrder"] = Tag(plan.TieBreakOrder ?? DefaultTieBreakOrder, Has("TieBreakOrder")),
            ["ClusterUnit"] = Tag(plan.ClusterUnit ?? "IDENTITY", Has("ClusterUnit")),
            ["Estimator"] = Tag(plan.Estimator ?? "LABELED", Has("Estimator")),
            ["ManualReviewSampleSize"] = Tag(plan.ManualReviewSampleSize ?? 0, Has("ManualReviewSampleSize")),
            ["RecalibrationTriggers"] = Tag(plan.RecalibrationTriggers ?? false, Has("RecalibrationTriggers")),
            ["PredeclaredCandidateCpMultiplicity"] = Tag(
                plan.PredeclaredCandidateCpMultiplicity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "CONSERVATIVE_CODE_BUDGET",
                Has("PredeclaredCandidateCpMultiplicity"))
        };
    }

    /// <summary>
    /// Explicitly reports effective option parameters from the deserialized
    /// runtime plan, *not* raw JSON assumptions about omitted fields.
    /// This is informational only and never enters model selection or SHA.
    /// </summary>
    public static IReadOnlyList<OptionReport> Describe(
        byte[] originalJson, V9NestedCalibrationPlan plan,
        IReadOnlyList<V9NestedCandidateEvaluation> evaluations)
    {
        ArgumentNullException.ThrowIfNull(originalJson);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(evaluations);
        ValidateRawInput(originalJson);
        using var doc = JsonDocument.Parse(originalJson);
        var root = doc.RootElement;
        var neutral = Neutral(root, plan);
        var sourceOptions = root.GetProperty("Calibration").GetProperty("ModelOptions")
            .EnumerateArray()
            .ToDictionary(x => x.GetProperty("OptionId").GetString()!, x => x, StringComparer.Ordinal);
        var effectiveOptions = plan.Calibration.ModelOptions
            .ToDictionary(x => x.OptionId, x => x, StringComparer.Ordinal);
        var optionParams = typeof(V9FsAdaptiveOptionConfiguration)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length).First().GetParameters()
            .Select(x => x.Name!).ToArray();
        var muSource = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (root.TryGetProperty("MuEstimationMethods", out var rawMu)
            && rawMu.ValueKind == JsonValueKind.Array)
            foreach (var method in rawMu.EnumerateArray())
                muSource.Add(method.GetProperty("Method").GetString()!, method);

        var result = new List<OptionReport>(evaluations.Count);
        foreach (var evaluation in evaluations)
        {
            var id = evaluation.OptionId;
            var cut = id?.IndexOf("__MU_", StringComparison.Ordinal) ?? -1;
            var baseId = cut >= 0 ? id![..cut] : id;
            var baseline = new SortedDictionary<string, Origin>(StringComparer.Ordinal);
            if (baseId is not null && sourceOptions.TryGetValue(baseId, out var rawOption)
                && effectiveOptions.TryGetValue(baseId, out var option))
            {
                foreach (var name in optionParams)
                {
                    var prop = typeof(V9FsAdaptiveOptionConfiguration).GetProperty(name,
                        BindingFlags.Instance | BindingFlags.Public)
                        ?? throw new InvalidDataException("Propriedade de parâmetro não encontrada: " + name);
                    baseline.Add(name, Tag(prop.GetValue(option),
                        rawOption.TryGetProperty(name, out _)));
                }
            }
            var mu = new SortedDictionary<string, Origin>(StringComparer.Ordinal);
            if (evaluation.MuEstimationMethod is { } muName
                && muSource.TryGetValue(muName, out var rawMethod)
                && plan.MuEstimationMethods?.FirstOrDefault(x => x.Method == muName) is { } effective)
            {
                mu["Method"] = Tag(effective.Method, rawMethod.TryGetProperty("Method", out _));
                mu["Role"] = Tag(effective.Role ?? V9MuEstimationSearchSpace.Candidate,
                    rawMethod.TryGetProperty("Role", out _));
                mu["Anchor"] = Tag(effective.Anchor, rawMethod.TryGetProperty("Anchor", out _));
                mu["Parameters"] = Tag(effective.Parameters, rawMethod.TryGetProperty("Parameters", out _));
            }
            result.Add(new(id, baseId, evaluation.MuEstimationMethod,
                baseline, mu, neutral));
        }
        return result.AsReadOnly();
    }
}
