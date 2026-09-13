using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Converte a frequência populacional da chave de publicação do nome em um estrato somente
/// quando o próprio modelo versionado fornece limites explícitos. Sem limites completos,
/// retorna UNKNOWN e o scorer usa os parâmetros marginais históricos.
/// </summary>
internal static class NameFrequencyStratification
{
    internal static NameFrequencyStratum Classify(
        IReadOnlyDictionary<string, decimal> parameters,
        string attribute,
        decimal? publicationKeyProbability)
    {
        if (publicationKeyProbability is null || publicationKeyProbability <= 0m || publicationKeyProbability >= 1m)
            return NameFrequencyStratum.UNKNOWN;

        if (!string.Equals(attribute, "NOME", StringComparison.Ordinal) &&
            !string.Equals(attribute, "NOME_MAE", StringComparison.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(attribute), attribute, "Atributo de frequência não suportado.");

        var rareName = $"FREQ_{attribute}_RARE_MAX_PROBABILITY";
        var uncommonName = $"FREQ_{attribute}_UNCOMMON_MAX_PROBABILITY";
        var commonName = $"FREQ_{attribute}_COMMON_MAX_PROBABILITY";

        if (!parameters.TryGetValue(rareName, out var rareMax) ||
            !parameters.TryGetValue(uncommonName, out var uncommonMax) ||
            !parameters.TryGetValue(commonName, out var commonMax))
            return NameFrequencyStratum.UNKNOWN;

        if (rareMax <= 0m || uncommonMax <= rareMax || commonMax <= uncommonMax || commonMax >= 1m)
            throw new InvalidOperationException($"Limites de estratificação de frequência inválidos para {attribute}.");

        var p = publicationKeyProbability.Value;
        if (p <= rareMax) return NameFrequencyStratum.RARE;
        if (p <= uncommonMax) return NameFrequencyStratum.UNCOMMON;
        if (p <= commonMax) return NameFrequencyStratum.COMMON;
        return NameFrequencyStratum.VERY_COMMON;
    }

    internal static decimal? ResolvePublicationKeyProbability(
        IReadOnlyDictionary<string, decimal>? frequencyByPublicationKey,
        string? fullName)
    {
        if (frequencyByPublicationKey is null || frequencyByPublicationKey.Count == 0)
            return null;

        var publicationKey = IbgeNamePublicationSemantics.ProjectFirstName(fullName);
        if (publicationKey is null)
            return null;

        return frequencyByPublicationKey.TryGetValue(publicationKey, out var probability)
            ? probability
            : null;
    }
}
