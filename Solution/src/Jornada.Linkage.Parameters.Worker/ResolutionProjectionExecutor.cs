using System.Globalization;
using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record ResolutionSourceValue(string Attribute, string Value);

/// <summary>
/// Executa somente projeções presentes no ResolutionProjectionPlan homologado.
/// Valores desconhecidos ou atributos inelegíveis são ignorados; nenhuma semântica é inferida
/// pelo nome do atributo. A saída é conjunto para tratar naturalmente atributos MULTI.
/// </summary>
public static class ResolutionProjectionExecutor
{
    public const string MethodVersion = "RESOLUTION_PROJECTION_EXECUTOR_V1";

    public static IReadOnlyList<BlockingProjectionKey> Project(
        ResolutionProjectionPlan plan,
        IEnumerable<ResolutionSourceValue> sourceValues)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sourceValues);

        var sources = plan.Sources.ToDictionary(static x => x.CanonicalCode, StringComparer.Ordinal);
        var featuresBySource = plan.Features
            .Where(static feature => feature.Origin == ResolutionFeatureOrigin.Calculated && feature.CandidateForBlocking)
            .GroupBy(static feature => feature.SourceAttribute, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var result = new HashSet<BlockingProjectionKey>();

        foreach (var sourceValue in sourceValues)
        {
            if (sourceValue is null || string.IsNullOrWhiteSpace(sourceValue.Attribute) || sourceValue.Value is null)
                continue;

            var code = ResolutionSourceField.Canonicalize(sourceValue.Attribute);
            if (!sources.TryGetValue(code, out var source) || !source.EligibleForResolution)
                continue;
            if (!featuresBySource.TryGetValue(code, out var features))
                continue;

            foreach (var feature in features)
            {
                foreach (var value in Execute(feature, sourceValue.Value))
                    if (!string.IsNullOrWhiteSpace(value))
                        result.Add(new BlockingProjectionKey(feature.Feature, value));
            }
        }

        return result
            .OrderBy(static key => key.Feature, StringComparer.Ordinal)
            .ThenBy(static key => key.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> Execute(ResolutionProjectedFeature feature, string value)
    {
        try
        {
            return ExecuteCore(feature, value).ToArray();
        }
        catch (InvalidDataException)
        {
            // Valor legado fora do contrato não ganha chave de blocking por aproximação.
            return Array.Empty<string>();
        }
        catch (FormatException)
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> ExecuteCore(ResolutionProjectedFeature feature, string value)
    {
        var output = feature.ProjectionOutput ?? throw new InvalidOperationException("Projeção calculada sem saída declarada.");
        return feature.Algorithm switch
        {
            "PERSON_NAME_BASIC_PTBR@V1" => ProjectBasicName(value, output),
            "PERSON_NAME_COMPONENTS@V2" => ProjectNameComponents(value, output),
            "PERSON_NAME_METAPHONE_BR@V1" => ProjectPhonetic(value, output),
            "DATE_COMPONENTS@V2" => ProjectDate(value, output),
            "TELEFONE_BR_CANONICO@V2" => output == "canonical"
                ? One(ContactCanonicalization.NormalizeBrazilianPhoneV2(value))
                : Array.Empty<string>(),
            "EMAIL_CANONICO@V2" => output == "canonical"
                ? One(ContactCanonicalization.NormalizeEmailV2(value))
                : Array.Empty<string>(),
            _ => throw new InvalidOperationException($"Algoritmo homologado sem executor: {feature.Algorithm}.")
        };
    }

    private static IEnumerable<string> ProjectBasicName(string value, string output)
    {
        var projection = PersonNameBasicNormalization.Project(value);
        if (projection is null)
            return Array.Empty<string>();
        return output switch
        {
            "upper" => One(projection.Upper),
            "upper_no_diacritics" => One(projection.UpperNoDiacritics),
            "without_particles" => One(projection.WithoutPortugueseParticles),
            _ => Array.Empty<string>()
        };
    }

    private static IEnumerable<string> ProjectNameComponents(string value, string output)
    {
        var normalized = IdentityComparison.NormalizeText(value);
        if (normalized is null)
            return Array.Empty<string>();
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return Array.Empty<string>();

        return output switch
        {
            "normalized" => One(normalized),
            "first" => One(tokens[0]),
            "last" => One(tokens[^1]),
            "surnames" when tokens.Length > 1 => tokens.Skip(1).Distinct(StringComparer.Ordinal).ToArray(),
            "surnames" => Array.Empty<string>(),
            _ => Array.Empty<string>()
        };
    }

    private static IEnumerable<string> ProjectPhonetic(string value, string output)
    {
        if (output != "phonetic")
            return Array.Empty<string>();
        var encoded = MetaphoneBr.Encode(value);
        return encoded is null ? Array.Empty<string>() : One(encoded);
    }

    private static IEnumerable<string> ProjectDate(string value, string output)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return Array.Empty<string>();
        return output switch
        {
            "day" => One(date.Day.ToString("D2", CultureInfo.InvariantCulture)),
            "month" => One(date.Month.ToString("D2", CultureInfo.InvariantCulture)),
            "year" => One(date.Year.ToString("D4", CultureInfo.InvariantCulture)),
            _ => Array.Empty<string>()
        };
    }

    private static string[] One(string value) => [value];
}
