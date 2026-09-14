namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Fonte canônica de proveniência de uma feature. LabelSource é preenchido apenas quando
/// o atributo de origem é explicitamente reconhecido como CPF ou CNS; demais atributos permanecem
/// como evidência independente com seu código canônico preservado.
/// </summary>
public sealed record GroundTruthLineageSource(
    string CanonicalAttribute,
    GroundTruthSource? LabelSource)
{
    public static GroundTruthLineageSource FromAttribute(string attribute)
    {
        if (string.IsNullOrWhiteSpace(attribute))
            throw new ArgumentException("O atributo-fonte da linhagem é obrigatório.", nameof(attribute));

        var canonical = Canonicalize(attribute);
        var source = canonical switch
        {
            "cpf" or "cpf_declarado" or "cpf_ancora" or "identificador_cpf" => GroundTruthSource.Cpf,
            "cns" or "cns_declarado" or "identificador_cns" => GroundTruthSource.Cns,
            _ => (GroundTruthSource?)null
        };

        return new GroundTruthLineageSource(canonical, source);
    }

    private static string Canonicalize(string value)
    {
        var buffer = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                buffer.Append(ch);
            else if (buffer.Length > 0 && buffer[^1] != '_')
                buffer.Append('_');
        }

        var result = buffer.ToString().Trim('_');
        if (result.Length == 0)
            throw new ArgumentException("O atributo-fonte não produz identificador canônico.", nameof(value));
        return result;
    }
}

/// <summary>
/// Proveniência explícita de uma feature usada em candidate generation, blocking ou scoring.
/// Sources deve conter as fontes transitivas conhecidas. Isso permite detectar leakage mesmo
/// quando o nome final da feature não carrega CPF/CNS.
/// </summary>
public sealed record GroundTruthFeatureLineage(
    string FeatureName,
    IReadOnlyCollection<GroundTruthLineageSource> Sources)
{
    public static GroundTruthFeatureLineage Direct(string featureName, params string[] sourceAttributes)
    {
        if (string.IsNullOrWhiteSpace(featureName))
            throw new ArgumentException("O nome da feature é obrigatório.", nameof(featureName));
        ArgumentNullException.ThrowIfNull(sourceAttributes);
        if (sourceAttributes.Length == 0)
            throw new ArgumentException("Ao menos um atributo-fonte é obrigatório.", nameof(sourceAttributes));

        var sources = sourceAttributes
            .Select(GroundTruthLineageSource.FromAttribute)
            .Distinct()
            .ToArray();

        return new GroundTruthFeatureLineage(featureName.Trim(), sources);
    }
}

public static class GroundTruthFeatureLineagePolicy
{
    public static void EnsureNoLabelLeakage(
        GroundTruthSource labelSource,
        IEnumerable<GroundTruthFeatureLineage> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        var normalized = features
            .Select(feature => feature ?? throw new ArgumentException(
                "Feature de linhagem nula não é permitida.", nameof(features)))
            .ToArray();

        var invalid = normalized
            .Where(feature => string.IsNullOrWhiteSpace(feature.FeatureName) ||
                feature.Sources is null || feature.Sources.Count == 0 || feature.Sources.Any(static source => source is null))
            .Select(feature => string.IsNullOrWhiteSpace(feature.FeatureName) ? "<sem_nome>" : feature.FeatureName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (invalid.Length > 0)
            throw new InvalidOperationException(
                $"Proveniência de feature inválida: {string.Join(", ", invalid)}. " +
                "Toda feature deve declarar nome e ao menos uma fonte canônica válida.");

        var leaking = normalized
            .Where(feature => feature.Sources.Any(source => source.LabelSource == labelSource))
            .Select(feature => feature.FeatureName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (leaking.Length > 0)
            throw new InvalidOperationException(
                $"Label leakage detectado por linhagem para {labelSource}: {string.Join(", ", leaking)}. " +
                "Nenhuma feature transitivamente derivada da fonte do rótulo pode participar de candidate generation, blocking ou score.");
    }
}
