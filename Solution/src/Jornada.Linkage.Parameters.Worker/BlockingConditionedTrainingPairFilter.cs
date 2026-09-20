using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Mantém, para estimação de u, somente não-vínculos que realmente sobreviveriam ao
/// ruleset escolhido. A álgebra é a mesma do runtime: AND entre campos de cada passe
/// e OR entre passes; ausência de qualquer campo torna o passe inelegível.
/// </summary>
public static class BlockingConditionedTrainingPairFilter
{
    public static IReadOnlyList<IdentityTrainingPair> Retain(
        IReadOnlyCollection<IdentityTrainingPair> pairs,
        IReadOnlyCollection<LinkageBlockingPass> passes)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(passes);
        if (passes.Count == 0)
            throw new ArgumentException("Ao menos um passe de blocking é obrigatório.", nameof(passes));

        var result = new List<IdentityTrainingPair>(pairs.Count);
        foreach (var pair in pairs)
        {
            var observation = BlockingFeatureObservationFactory.Create(pair, isReferenceMatch: false);
            if (Retains(observation, passes))
                result.Add(pair);
        }
        return result;
    }

    public static bool RetainsProjectedKeys(
        IReadOnlyDictionary<string, IReadOnlySet<string>> leftKeys,
        IReadOnlyDictionary<string, IReadOnlySet<string>> rightKeys,
        IEnumerable<LinkageBlockingPass> passes)
    {
        ArgumentNullException.ThrowIfNull(leftKeys);
        ArgumentNullException.ThrowIfNull(rightKeys);
        ArgumentNullException.ThrowIfNull(passes);

        foreach (var pass in passes)
        {
            var retained = true;
            foreach (var field in pass.Fields)
            {
                if (!leftKeys.TryGetValue(field, out var leftValues) ||
                    !rightKeys.TryGetValue(field, out var rightValues) ||
                    leftValues.Count == 0 ||
                    rightValues.Count == 0 ||
                    !leftValues.Overlaps(rightValues))
                {
                    retained = false;
                    break;
                }
            }

            if (retained)
                return true;
        }

        return false;
    }

    public static bool Retains(
        BlockingFeatureObservation observation,
        IEnumerable<LinkageBlockingPass> passes)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(observation.Agreements);
        ArgumentNullException.ThrowIfNull(passes);

        foreach (var pass in passes)
        {
            var retained = true;
            foreach (var field in pass.Fields)
            {
                if (!observation.Agreements.TryGetValue(field, out var agreement) || agreement is not true)
                {
                    retained = false;
                    break;
                }
            }

            if (retained)
                return true;
        }

        return false;
    }
}
