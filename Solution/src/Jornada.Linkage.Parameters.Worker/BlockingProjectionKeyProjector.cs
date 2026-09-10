using System.Globalization;
using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record BlockingProjectionKey(string Feature, string Value);

/// <summary>
/// Projeta chaves derivadas de blocking a partir dos campos canônicos da Gold.
/// Não decide identidade, não altera a normalização e não aplica heurísticas ocultas.
/// A saída é reconstruível para a mesma IdentityComparison.NormalizationVersion.
/// </summary>
public static class BlockingProjectionKeyProjector
{
    public const string MethodVersion = "BLOCKING_PROJECTION_KEY_PROJECTOR_V1";

    public static IReadOnlyList<BlockingProjectionKey> Project(
        string? fullName,
        string? motherName,
        DateOnly birthDate)
    {
        var keys = new HashSet<BlockingProjectionKey>();

        AddNameComponents(
            keys,
            fullName,
            BlockingCandidateFeatureCatalog.FirstName,
            BlockingCandidateFeatureCatalog.Surnames,
            BlockingCandidateFeatureCatalog.LastName);

        AddNameComponents(
            keys,
            motherName,
            BlockingCandidateFeatureCatalog.MotherFirstName,
            BlockingCandidateFeatureCatalog.MotherSurnames,
            BlockingCandidateFeatureCatalog.MotherLastName);

        keys.Add(new BlockingProjectionKey(
            BlockingCandidateFeatureCatalog.BirthDay,
            birthDate.Day.ToString("D2", CultureInfo.InvariantCulture)));
        keys.Add(new BlockingProjectionKey(
            BlockingCandidateFeatureCatalog.BirthMonth,
            birthDate.Month.ToString("D2", CultureInfo.InvariantCulture)));
        keys.Add(new BlockingProjectionKey(
            BlockingCandidateFeatureCatalog.BirthYear,
            birthDate.Year.ToString("D4", CultureInfo.InvariantCulture)));

        return keys
            .OrderBy(static key => key.Feature, StringComparer.Ordinal)
            .ThenBy(static key => key.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddNameComponents(
        HashSet<BlockingProjectionKey> keys,
        string? value,
        string firstNameFeature,
        string surnamesFeature,
        string lastNameFeature)
    {
        var normalized = IdentityComparison.NormalizeText(value);
        if (normalized is null)
            return;

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return;

        keys.Add(new BlockingProjectionKey(firstNameFeature, tokens[0]));

        if (tokens.Length == 1)
        {
            keys.Add(new BlockingProjectionKey(lastNameFeature, tokens[0]));
            return;
        }

        for (var index = 1; index < tokens.Length; index++)
            keys.Add(new BlockingProjectionKey(surnamesFeature, tokens[index]));

        keys.Add(new BlockingProjectionKey(lastNameFeature, tokens[^1]));
    }
}
