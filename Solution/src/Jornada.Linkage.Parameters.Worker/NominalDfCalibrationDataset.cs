using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record NominalDfCalibrationDataset(
    IReadOnlyList<DfCalibrationObservation> Observations,
    long MatchedInputCount,
    long UnmatchedInputCount,
    long FrequencyCensoredCount,
    long PublishedFirstNameOccurrences,
    decimal ReferenceExactUProbability,
    string PublicationMethodVersion,
    string NormalizationVersion,
    string EvidenceAlgorithmVersion,
    string TermFrequencyAlgorithmVersion);

/// <summary>
/// Constrói o dataset rotulado do primeiro estágio DF usando somente a semântica
/// publicável que a Jornada consegue derivar sem inventar fronteira de sobrenome:
/// o primeiro nome segundo IBGE_CENSO_2022_NOMES_PUBLICACAO_V1.
///
/// Frequências ausentes da publicação não são tratadas como zero. O par permanece
/// no dataset com FrequencyCensored=true e sem ajuste TF, portanto é inconclusivo
/// para qualquer fronteira DF e segue ao estágio FS.
/// </summary>
public static class NominalDfCalibrationDatasetFactory
{
    public static NominalDfCalibrationDataset Create(
        IReadOnlyList<IdentityTrainingPair> matchedPairs,
        IReadOnlyList<IdentityTrainingPair> unmatchedPairs,
        IEnumerable<IbgeTypedNameFrequencyEntry> referenceEntries,
        decimal tfWeight = 1m,
        decimal tfMinimumUValue = 0m)
    {
        ArgumentNullException.ThrowIfNull(matchedPairs);
        ArgumentNullException.ThrowIfNull(unmatchedPairs);
        ArgumentNullException.ThrowIfNull(referenceEntries);

        if (matchedPairs.Count == 0)
            throw new ArgumentException("Dataset DF exige ao menos um par MATCH rotulado.", nameof(matchedPairs));
        if (unmatchedPairs.Count == 0)
            throw new ArgumentException("Dataset DF exige ao menos um par NON_MATCH rotulado.", nameof(unmatchedPairs));
        ArgumentOutOfRangeException.ThrowIfLessThan(tfWeight, 0m);
        ArgumentOutOfRangeException.ThrowIfLessThan(tfMinimumUValue, 0m);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tfMinimumUValue, 1m);

        var frequencies = referenceEntries
            .Where(static entry =>
                entry.StatisticKind == IbgeNameStatisticKind.FirstName &&
                entry.Occurrences > 0)
            .Select(static entry => new
            {
                Name = IdentityComparison.NormalizeText(entry.Name),
                entry.Occurrences
            })
            .Where(static entry => entry.Name is not null)
            .GroupBy(static entry => entry.Name!, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => checked(group.Sum(item => item.Occurrences)),
                StringComparer.Ordinal);

        if (frequencies.Count == 0)
            throw new ArgumentException(
                "Referência nominal não contém frequências positivas de primeiro nome.",
                nameof(referenceEntries));

        var totalOccurrences = checked(frequencies.Values.Sum());
        if (totalOccurrences <= 0)
            throw new InvalidOperationException("Total de ocorrências de primeiro nome deve ser positivo.");

        var probabilities = frequencies.ToDictionary(
            static pair => pair.Key,
            pair => (decimal)pair.Value / totalOccurrences,
            StringComparer.Ordinal);

        decimal referenceExactU = 0m;
        foreach (var probability in probabilities.Values)
            referenceExactU += probability * probability;

        if (referenceExactU <= 0m || referenceExactU > 1m)
            throw new InvalidOperationException("Probabilidade u exata da projeção de primeiro nome ficou fora do domínio.");

        var observations = new List<DfCalibrationObservation>(
            checked(matchedPairs.Count + unmatchedPairs.Count));
        long censored = 0;

        Add(matchedPairs, isTrueMatch: true);
        Add(unmatchedPairs, isTrueMatch: false);

        return new NominalDfCalibrationDataset(
            observations,
            matchedPairs.Count,
            unmatchedPairs.Count,
            censored,
            totalOccurrences,
            referenceExactU,
            IbgeNamePublicationSemantics.MethodVersion,
            IdentityComparison.NormalizationVersion,
            NominalDfEvidenceCalculator.AlgorithmVersion,
            SplinkCompatibleTermFrequency.AlgorithmVersion);

        void Add(IEnumerable<IdentityTrainingPair> pairs, bool isTrueMatch)
        {
            foreach (var pair in pairs)
            {
                var left = IbgeNamePublicationSemantics.ProjectFirstName(pair.LeftName);
                var right = IbgeNamePublicationSemantics.ProjectFirstName(pair.RightName);

                decimal? leftFrequency = null;
                decimal? rightFrequency = null;

                if (left is not null && probabilities.TryGetValue(left.FirstNameNormalized, out var lf))
                    leftFrequency = lf;
                if (right is not null && probabilities.TryGetValue(right.FirstNameNormalized, out var rf))
                    rightFrequency = rf;

                var frequencyCensored =
                    left is null ||
                    right is null ||
                    !leftFrequency.HasValue ||
                    !rightFrequency.HasValue;

                if (frequencyCensored)
                    censored++;

                var evidence = NominalDfEvidenceCalculator.Evaluate(
                    left?.FirstNameNormalized,
                    right?.FirstNameNormalized,
                    leftFrequency,
                    rightFrequency,
                    referenceExactU,
                    tfWeight,
                    tfMinimumUValue,
                    frequencyCensored);

                observations.Add(new DfCalibrationObservation(isTrueMatch, evidence));
            }
        }
    }
}
