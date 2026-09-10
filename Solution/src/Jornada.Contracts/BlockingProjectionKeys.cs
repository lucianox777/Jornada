using System.Globalization;

namespace Jornada.Contracts;

/// <summary>
/// Vocabulário físico-lógico compartilhado entre Processor, Calibrador, Avaliador e Runner.
/// </summary>
public static class BlockingFeatureNames
{
    public const string FullName = "name_full";
    public const string FirstName = "name_first";
    public const string Surnames = "name_surnames";
    public const string LastName = "name_last";
    public const string MotherFullName = "mother_name_full";
    public const string MotherFirstName = "mother_name_first";
    public const string MotherSurnames = "mother_name_surnames";
    public const string MotherLastName = "mother_name_last";
    public const string BirthDay = "birth_day";
    public const string BirthMonth = "birth_month";
    public const string BirthYear = "birth_year";
}

/// <summary>
/// Semântica temporal do atributo usado no blocking.
/// StableIdentityDatum: espera-se estabilidade ao longo da vida; mudança representa correção excepcional.
/// VersionedAlias: o valor pode mudar legitimamente e valores anteriores podem permanecer úteis para recuperação de candidatos.
/// </summary>
public enum BlockingFeatureTemporalSemantics
{
    StableIdentityDatum,
    VersionedAlias
}

public static class BlockingFeatureTemporalCatalog
{
    public const string MethodVersion = "BLOCKING_FEATURE_TEMPORAL_CATALOG_V1";

    public static BlockingFeatureTemporalSemantics Get(string feature) => feature switch
    {
        BlockingFeatureNames.BirthDay or
        BlockingFeatureNames.BirthMonth or
        BlockingFeatureNames.BirthYear => BlockingFeatureTemporalSemantics.StableIdentityDatum,

        BlockingFeatureNames.FullName or
        BlockingFeatureNames.FirstName or
        BlockingFeatureNames.Surnames or
        BlockingFeatureNames.LastName or
        BlockingFeatureNames.MotherFullName or
        BlockingFeatureNames.MotherFirstName or
        BlockingFeatureNames.MotherSurnames or
        BlockingFeatureNames.MotherLastName => BlockingFeatureTemporalSemantics.VersionedAlias,

        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown blocking feature.")
    };
}

public sealed record BlockingProjectionKey(string Feature, string Value);

/// <summary>
/// Projeta chaves derivadas de blocking a partir dos campos canônicos da Gold.
/// Não decide identidade e reutiliza exatamente IdentityComparison.NormalizeText.
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
            BlockingFeatureNames.FirstName,
            BlockingFeatureNames.Surnames,
            BlockingFeatureNames.LastName);

        AddNameComponents(
            keys,
            motherName,
            BlockingFeatureNames.MotherFirstName,
            BlockingFeatureNames.MotherSurnames,
            BlockingFeatureNames.MotherLastName);

        keys.Add(new BlockingProjectionKey(
            BlockingFeatureNames.BirthDay,
            birthDate.Day.ToString("D2", CultureInfo.InvariantCulture)));
        keys.Add(new BlockingProjectionKey(
            BlockingFeatureNames.BirthMonth,
            birthDate.Month.ToString("D2", CultureInfo.InvariantCulture)));
        keys.Add(new BlockingProjectionKey(
            BlockingFeatureNames.BirthYear,
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
