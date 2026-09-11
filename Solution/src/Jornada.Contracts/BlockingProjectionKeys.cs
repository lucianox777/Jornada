using System.Globalization;

namespace Jornada.Contracts;

/// <summary>
/// Vocabulário físico-lógico compartilhado entre Processor, Calibrador, Avaliador e Runner.
/// </summary>
public static class BlockingFeatureNames
{
    public const string FullName = "name_full";
    public const string FullNameUpper = "name_upper";
    public const string FullNameUpperNoDiacritics = "name_upper_no_diacritics";
    public const string FullNameWithoutParticles = "name_without_particles";
    public const string FirstName = "name_first";
    public const string Surnames = "name_surnames";
    public const string LastName = "name_last";
    public const string MotherFullName = "mother_name_full";
    public const string MotherFullNameUpper = "mother_name_upper";
    public const string MotherFullNameUpperNoDiacritics = "mother_name_upper_no_diacritics";
    public const string MotherFullNameWithoutParticles = "mother_name_without_particles";
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
    public const string MethodVersion = "BLOCKING_FEATURE_TEMPORAL_CATALOG_V2";

    public static BlockingFeatureTemporalSemantics Get(string feature) => feature switch
    {
        BlockingFeatureNames.BirthDay or
        BlockingFeatureNames.BirthMonth or
        BlockingFeatureNames.BirthYear => BlockingFeatureTemporalSemantics.StableIdentityDatum,

        BlockingFeatureNames.FullName or
        BlockingFeatureNames.FullNameUpper or
        BlockingFeatureNames.FullNameUpperNoDiacritics or
        BlockingFeatureNames.FullNameWithoutParticles or
        BlockingFeatureNames.FirstName or
        BlockingFeatureNames.Surnames or
        BlockingFeatureNames.LastName or
        BlockingFeatureNames.MotherFullName or
        BlockingFeatureNames.MotherFullNameUpper or
        BlockingFeatureNames.MotherFullNameUpperNoDiacritics or
        BlockingFeatureNames.MotherFullNameWithoutParticles or
        BlockingFeatureNames.MotherFirstName or
        BlockingFeatureNames.MotherSurnames or
        BlockingFeatureNames.MotherLastName => BlockingFeatureTemporalSemantics.VersionedAlias,

        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown blocking feature.")
    };
}

public sealed record BlockingProjectionKey(string Feature, string Value);

/// <summary>
/// Projeta chaves derivadas de blocking a partir dos campos canônicos da Gold/Silver.
/// As representações básicas de nome permanecem separadas para que o Calibrador consiga
/// medir UPPER, remoção de diacríticos e remoção de partículas portuguesas sem destruir o original.
/// </summary>
public static class BlockingProjectionKeyProjector
{
    public const string MethodVersion = "BLOCKING_PROJECTION_KEY_PROJECTOR_V3";

    public static IReadOnlyList<BlockingProjectionKey> Project(
        string? fullName,
        string? motherName,
        DateOnly birthDate)
    {
        var keys = new HashSet<BlockingProjectionKey>();

        AddBasicNameRepresentations(
            keys,
            fullName,
            BlockingFeatureNames.FullNameUpper,
            BlockingFeatureNames.FullNameUpperNoDiacritics,
            BlockingFeatureNames.FullNameWithoutParticles);

        AddNameComponents(
            keys,
            fullName,
            BlockingFeatureNames.FullName,
            BlockingFeatureNames.FirstName,
            BlockingFeatureNames.Surnames,
            BlockingFeatureNames.LastName);

        AddBasicNameRepresentations(
            keys,
            motherName,
            BlockingFeatureNames.MotherFullNameUpper,
            BlockingFeatureNames.MotherFullNameUpperNoDiacritics,
            BlockingFeatureNames.MotherFullNameWithoutParticles);

        AddNameComponents(
            keys,
            motherName,
            BlockingFeatureNames.MotherFullName,
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

    private static void AddBasicNameRepresentations(
        HashSet<BlockingProjectionKey> keys,
        string? value,
        string upperFeature,
        string noDiacriticsFeature,
        string withoutParticlesFeature)
    {
        var projection = PersonNameBasicNormalization.Project(value);
        if (projection is null)
            return;

        keys.Add(new BlockingProjectionKey(upperFeature, projection.Upper));
        keys.Add(new BlockingProjectionKey(noDiacriticsFeature, projection.UpperNoDiacritics));
        keys.Add(new BlockingProjectionKey(withoutParticlesFeature, projection.WithoutPortugueseParticles));
    }

    private static void AddNameComponents(
        HashSet<BlockingProjectionKey> keys,
        string? value,
        string fullNameFeature,
        string firstNameFeature,
        string surnamesFeature,
        string lastNameFeature)
    {
        var normalized = IdentityComparison.NormalizeText(value);
        if (normalized is null)
            return;

        keys.Add(new BlockingProjectionKey(fullNameFeature, normalized));

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
