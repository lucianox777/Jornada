using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Converte os pares M/U do Calibrador em concordâncias das mesmas chaves lógicas
/// usadas pelo Processor/Runner. Não imputa campo ausente e não decide a política.
/// </summary>
public static class BlockingFeatureObservationFactory
{
    public const string MethodVersion = "BLOCKING_FEATURE_OBSERVATION_FACTORY_V1";

    public static IReadOnlyList<BlockingFeatureObservation> Create(
        IReadOnlyCollection<IdentityTrainingPair> matchedPairs,
        IReadOnlyCollection<IdentityTrainingPair> unmatchedPairs)
    {
        ArgumentNullException.ThrowIfNull(matchedPairs);
        ArgumentNullException.ThrowIfNull(unmatchedPairs);
        if (matchedPairs.Count == 0)
            throw new ArgumentException("Ao menos um par verdadeiro é obrigatório.", nameof(matchedPairs));
        if (unmatchedPairs.Count == 0)
            throw new ArgumentException("Ao menos um não-vínculo é obrigatório.", nameof(unmatchedPairs));

        return matchedPairs.Select(static pair => Create(pair, true))
            .Concat(unmatchedPairs.Select(static pair => Create(pair, false)))
            .ToArray();
    }

    public static BlockingFeatureObservation Create(
        IdentityTrainingPair pair,
        bool isReferenceMatch,
        decimal weight = 1m)
    {
        ArgumentNullException.ThrowIfNull(pair);
        if (weight <= 0m)
            throw new ArgumentOutOfRangeException(nameof(weight), "O peso deve ser positivo.");

        var left = Project(pair.LeftName, pair.LeftMotherName, pair.LeftBirthDate);
        var right = Project(pair.RightName, pair.RightMotherName, pair.RightBirthDate);
        var agreements = new Dictionary<string, bool?>(StringComparer.Ordinal);

        foreach (var feature in BlockingCandidateFeatureCatalog.RequiredOptimizerCandidates)
        {
            var hasLeft = left.TryGetValue(feature, out var leftValues) && leftValues.Count > 0;
            var hasRight = right.TryGetValue(feature, out var rightValues) && rightValues.Count > 0;
            agreements[feature] = hasLeft && hasRight
                ? leftValues!.Overlaps(rightValues!)
                : null;
        }

        return new BlockingFeatureObservation(isReferenceMatch, agreements, weight);
    }

    private static Dictionary<string, HashSet<string>> Project(
        string? name,
        string? motherName,
        DateOnly birthDate) =>
        BlockingProjectionKeyProjector.Project(name, motherName, birthDate)
            .GroupBy(static key => key.Feature, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static key => key.Value).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
}
