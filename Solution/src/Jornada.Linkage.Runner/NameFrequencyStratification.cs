using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Converte a frequência populacional da chave de publicação do nome em um estrato somente
/// quando o próprio modelo versionado fornece limites explícitos. Sem limites completos,
/// retorna UNKNOWN e o scorer usa os parâmetros marginais históricos.
/// </summary>
internal static class NameFrequencyStratification
{
    private const string RareMax = "FREQ_NOME_RARE_MAX_PROBABILITY";
    private const string UncommonMax = "FREQ_NOME_UNCOMMON_MAX_PROBABILITY";
    private const string CommonMax = "FREQ_NOME_COMMON_MAX_PROBABILITY";

    internal static NameFrequencyStratum Classify(
        IReadOnlyDictionary<string, decimal> parameters,
        decimal? publicationKeyProbability)
    {
        if (publicationKeyProbability is null || publicationKeyProbability <= 0m || publicationKeyProbability >= 1m)
            return NameFrequencyStratum.UNKNOWN;

        if (!parameters.TryGetValue(RareMax, out var rareMax) ||
            !parameters.TryGetValue(UncommonMax, out var uncommonMax) ||
            !parameters.TryGetValue(CommonMax, out var commonMax))
            return NameFrequencyStratum.UNKNOWN;

        if (rareMax <= 0m || uncommonMax <= rareMax || commonMax <= uncommonMax || commonMax >= 1m)
            throw new InvalidOperationException("Limites de estratificação de frequência do nome inválidos no modelo.");

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
