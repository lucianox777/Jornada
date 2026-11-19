using System.IO;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Decision GOV-DOC-01: explicit context-population substitution for nominal
/// terms absent from the frozen published IBGE marginal. This is an imputation,
/// not an observed IBGE count or a cap on the resulting TF log adjustment.
/// </summary>
public static class V9UnpublishedNominalFrequency
{
    public const long DefaultContextPopulation = 11911337;
    public const long AssumedUnpublishedOccurrences = 20;
    public const long MunicipalPublicationThreshold = 10;
    public const string PolicyVersion = "V9_IBGE_UNPUBLISHED_CONTEXT_POPULATION_20_V1";

    public static decimal Resolve(
        IReadOnlyDictionary<string, decimal> publishedFrequencies,
        string normalizedTerm, long contextPopulation = DefaultContextPopulation,
        long unpublishedOccurrenceCount = AssumedUnpublishedOccurrences)
    {
        if (unpublishedOccurrenceCount < 1 || contextPopulation < unpublishedOccurrenceCount)
            throw new InvalidDataException("Ocorrências imputadas devem estar entre 1 e a população contextual.");
        if (string.IsNullOrWhiteSpace(normalizedTerm) || publishedFrequencies is null)
            throw new InvalidDataException("Termo nominal ou referência IBGE inválidos.");
        if (publishedFrequencies.TryGetValue(normalizedTerm, out var frequency))
        {
            if (frequency <= 0m || frequency > 1m)
                throw new InvalidDataException("Frequência IBGE publicada inválida.");
            return frequency;
        }
        return (decimal)unpublishedOccurrenceCount / contextPopulation;
    }
}
