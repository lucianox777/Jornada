namespace Jornada.Contracts;

/// <summary>
/// Semântica declarada do atributo para resolução. O código/nome do campo nunca é usado
/// para inferir semântica ou elegibilidade.
/// </summary>
public enum PersonResolutionSemantic
{
    PersonName,
    Date,
    Phone,
    Email,
    Address,
    Text,
    Categorical
}

public sealed record PersonResolutionAttributeContract(
    string Code,
    PersonResolutionSemantic Semantic,
    string? CompatibilityProfile,
    bool EligibleForResolution,
    bool MultiValued,
    BlockingFeatureTemporalSemantics? BlockingTemporalSemantics,
    IReadOnlyList<string> BlockingFeatures);

/// <summary>
/// Contrato compartilhado entre Processor, Calibrador e Runner. É a fonte única para
/// elegibilidade de atributos de Pessoa e para o vocabulário físico de blocking exposto
/// ao runtime. Atributo ausente deste catálogo ou sem elegibilidade falha fechado.
/// </summary>
public static class PersonResolutionContractCatalog
{
    public const string CatalogVersion = "PERSON_RESOLUTION_CONTRACT_CATALOG_V1";

    public const string FullName = "nome_completo";
    public const string MotherName = "nome_mae";
    public const string BirthDate = "data_nascimento";
    public const string ResidentialAddress = "ENDERECO_RESIDENCIAL";
    public const string ConfidentialShelterAddress = "ENDERECO_CASA_ABRIGO_SIGILOSA";
    public const string TerritorialReference = "REFERENCIA_TERRITORIAL";
    public const string ContactPhone = "TELEFONE_CONTATO";
    public const string ContactEmail = "EMAIL_CONTATO";
    public const string SocialName = "NOME_SOCIAL";

    public const string ContactPhoneCanonicalFeature = "telefone_contato__canonical";
    public const string ContactEmailCanonicalFeature = "email_contato__canonical";
    public const string SocialNameUpperFeature = "nome_social__upper";
    public const string SocialNameUpperNoDiacriticsFeature = "nome_social__upper_no_diacritics";
    public const string SocialNameWithoutParticlesFeature = "nome_social__without_particles";
    public const string SocialNameNormalizedFeature = "nome_social__normalized";
    public const string SocialNameFirstFeature = "nome_social__first";
    public const string SocialNameSurnamesFeature = "nome_social__surnames";
    public const string SocialNameLastFeature = "nome_social__last";
    public const string SocialNamePhoneticFeature = "nome_social__phonetic";

    private static readonly PersonResolutionAttributeContract[] Fields =
    {
        new(
            FullName,
            PersonResolutionSemantic.PersonName,
            "PERSON_NAME",
            EligibleForResolution: true,
            MultiValued: false,
            BlockingFeatureTemporalSemantics.VersionedAlias,
            new[]
            {
                BlockingFeatureNames.FullName,
                BlockingFeatureNames.FullNameUpper,
                BlockingFeatureNames.FullNameUpperNoDiacritics,
                BlockingFeatureNames.FullNameWithoutParticles,
                BlockingFeatureNames.FullNamePhoneticPtBr,
                BlockingFeatureNames.FirstName,
                BlockingFeatureNames.Surnames,
                BlockingFeatureNames.LastName
            }),
        new(
            MotherName,
            PersonResolutionSemantic.PersonName,
            "MOTHER_NAME",
            EligibleForResolution: true,
            MultiValued: false,
            BlockingFeatureTemporalSemantics.VersionedAlias,
            new[]
            {
                BlockingFeatureNames.MotherFullName,
                BlockingFeatureNames.MotherFullNameUpper,
                BlockingFeatureNames.MotherFullNameUpperNoDiacritics,
                BlockingFeatureNames.MotherFullNameWithoutParticles,
                BlockingFeatureNames.MotherFullNamePhoneticPtBr,
                BlockingFeatureNames.MotherFirstName,
                BlockingFeatureNames.MotherSurnames,
                BlockingFeatureNames.MotherLastName
            }),
        new(
            BirthDate,
            PersonResolutionSemantic.Date,
            "BIRTH_DATE",
            EligibleForResolution: true,
            MultiValued: false,
            BlockingFeatureTemporalSemantics.StableIdentityDatum,
            new[]
            {
                BlockingFeatureNames.BirthDay,
                BlockingFeatureNames.BirthMonth,
                BlockingFeatureNames.BirthYear
            }),

        // Permanecem conhecidos semanticamente, porém inelegíveis. Em especial, casa-abrigo
        // não pode surgir no linkage por convenção nominal, formato ou presença no payload.
        new(ResidentialAddress, PersonResolutionSemantic.Address, null, false, false, null, Array.Empty<string>()),
        new(ConfidentialShelterAddress, PersonResolutionSemantic.Address, null, false, false, null, Array.Empty<string>()),
        new(TerritorialReference, PersonResolutionSemantic.Categorical, null, false, false, null, Array.Empty<string>()),

        new(
            ContactPhone,
            PersonResolutionSemantic.Phone,
            null,
            EligibleForResolution: true,
            MultiValued: true,
            BlockingFeatureTemporalSemantics.VersionedAlias,
            new[] { ContactPhoneCanonicalFeature }),
        new(
            ContactEmail,
            PersonResolutionSemantic.Email,
            null,
            EligibleForResolution: true,
            MultiValued: true,
            BlockingFeatureTemporalSemantics.VersionedAlias,
            new[] { ContactEmailCanonicalFeature }),
        new(
            SocialName,
            PersonResolutionSemantic.PersonName,
            null,
            EligibleForResolution: true,
            MultiValued: false,
            BlockingFeatureTemporalSemantics.VersionedAlias,
            new[]
            {
                SocialNameUpperFeature,
                SocialNameUpperNoDiacriticsFeature,
                SocialNameWithoutParticlesFeature,
                SocialNameNormalizedFeature,
                SocialNameFirstFeature,
                SocialNameSurnamesFeature,
                SocialNameLastFeature,
                SocialNamePhoneticFeature
            })
    };

    private static readonly IReadOnlyDictionary<string, PersonResolutionAttributeContract> ByCode = Fields
        .ToDictionary(static field => field.Code, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, PersonResolutionAttributeContract> ByBlockingFeature = Fields
        .Where(static field => field.EligibleForResolution && field.BlockingTemporalSemantics.HasValue)
        .SelectMany(static field => field.BlockingFeatures.Select(feature => (Feature: feature, Field: field)))
        .ToDictionary(static item => item.Feature, static item => item.Field, StringComparer.Ordinal);

    public static IReadOnlyList<PersonResolutionAttributeContract> All => Fields;

    public static IReadOnlyList<PersonResolutionAttributeContract> Eligible => Fields
        .Where(static field => field.EligibleForResolution)
        .OrderBy(static field => field.Code, StringComparer.Ordinal)
        .ToArray();

    public static IReadOnlyList<PersonResolutionAttributeContract> EligibleTransversal => Eligible
        .Where(static field => field.CompatibilityProfile is null)
        .ToArray();

    public static bool TryGet(string? code, out PersonResolutionAttributeContract field)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            field = null!;
            return false;
        }

        return ByCode.TryGetValue(code.Trim(), out field!);
    }

    public static bool IsEligible(string? code) =>
        TryGet(code, out var field) && field.EligibleForResolution;

    public static bool TryGetByBlockingFeature(string? feature, out PersonResolutionAttributeContract field)
    {
        if (string.IsNullOrWhiteSpace(feature))
        {
            field = null!;
            return false;
        }

        return ByBlockingFeature.TryGetValue(feature.Trim(), out field!);
    }
}
