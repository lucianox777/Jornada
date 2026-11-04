using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// An explicitly declared sensitivity assumption, NEVER an IBGE observation.
/// For an exact nominal agreement absent from the pinned publication, use the
/// smallest strictly positive PUBLISHED frequency from that same side/unit,
/// bounded below by the model's predeclared TF minimum. The missing value is
/// still absent from the published-frequency map and from every ref table.
/// </summary>
public static class V9TfCensoredFloorSensitivity
{
    public const string Version = "V9_CENSORED_PUBLISHED_FLOOR_SENSITIVITY_V1";

    public static decimal RequirePublishedFloor(IReadOnlyDictionary<string, decimal> published)
    {
        ArgumentNullException.ThrowIfNull(published);
        if (published.Count == 0 || published.Values.Any(x => x <= 0m || x > 1m))
            throw new InvalidDataException("Piso sensibilidade exige marginal IBGE publicada valida.");
        return published.Values.Min();
    }

    public static (decimal Frequency, V9TfFrequencyEvidenceV9 Evidence) Resolve(
        IReadOnlyDictionary<string, decimal> published,
        string normalizedTerm,
        decimal minimumPublished,
        decimal configuredMinimum,
        V9TfUnpublishedTermPolicyV9 policy)
    {
        ArgumentNullException.ThrowIfNull(published);
        // Sensitivity requires an actual published marginal for this slot.
        // Keep the minimum cached: no per-candidate scan of the full map.
        if (published.Count == 0)
            throw new InvalidDataException("Piso sensibilidade sem marginal IBGE publicada.");
        if (string.IsNullOrWhiteSpace(normalizedTerm)
            || minimumPublished <= 0m || minimumPublished > 1m
            || configuredMinimum < 0m || configuredMinimum > 1m
            || !Enum.IsDefined(policy))
            throw new InvalidDataException("Hipotese TF requer unidade, minimo e politica validos.");

        if (published.TryGetValue(normalizedTerm, out var observed))
        {
            if (observed <= 0m || observed > 1m)
                throw new InvalidDataException("Frequencia IBGE publicada invalida.");
            return (observed, V9TfFrequencyEvidenceV9.Published);
        }
        if (policy != V9TfUnpublishedTermPolicyV9.CensoredFloorSensitivity)
            throw new InvalidDataException("TF positiva sem frequencia IBGE publicada.");
        return (Math.Max(minimumPublished, configuredMinimum),
            V9TfFrequencyEvidenceV9.CensoredFloorHypothesis);
    }
}
