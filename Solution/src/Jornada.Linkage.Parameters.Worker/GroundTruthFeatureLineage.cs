namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Proveniência explícita de uma feature usada em candidate generation, blocking ou scoring.
/// A lista SourceAttributes deve conter os atributos-fonte transitivos conhecidos, não apenas
/// o nome final da feature. Isso permite detectar leakage mesmo quando o derivado não carrega
/// CPF/CNS no próprio nome.
/// </summary>
public sealed record GroundTruthFeatureLineage(
    string FeatureName,
    IReadOnlyCollection<string> SourceAttributes)
{
    public static GroundTruthFeatureLineage Direct(string featureName, params string[] sourceAttributes) =>
        new(featureName, sourceAttributes);
}

public static class GroundTruthFeatureLineagePolicy
{
    public static void EnsureNoLabelLeakage(
        GroundTruthSource labelSource,
        IEnumerable<GroundTruthFeatureLineage> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        var forbidden = labelSource.ToString();
        var leaking = features
            .Where(static feature => feature is not null)
            .Where(feature => feature.SourceAttributes.Any(source =>
                !string.IsNullOrWhiteSpace(source) &&
                string.Equals(source.Trim(), forbidden, StringComparison.OrdinalIgnoreCase)))
            .Select(feature => feature.FeatureName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (leaking.Length > 0)
            throw new InvalidOperationException(
                $"Label leakage detectado por linhagem para {labelSource}: {string.Join(", ", leaking)}. " +
                "Nenhuma feature transitivamente derivada da fonte do rótulo pode participar de candidate generation, blocking ou score.");
    }
}
