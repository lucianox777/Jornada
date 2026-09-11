using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Visão do Calibrador sobre o contrato compartilhado de atributos de Pessoa. Elegibilidade,
/// cardinalidade e códigos pertencem a Jornada.Contracts; aqui apenas adaptamos a semântica
/// para o planner científico/homologado do Calibrador.
/// </summary>
public static class PersonResolutionAttributeCatalog
{
    public const string CatalogVersion = PersonResolutionContractCatalog.CatalogVersion;
    public const string ProjectionSchemaVersion = "PERSON_RESOLUTION_PROJECTION_V2";

    public const string FullName = PersonResolutionContractCatalog.FullName;
    public const string MotherName = PersonResolutionContractCatalog.MotherName;
    public const string BirthDate = PersonResolutionContractCatalog.BirthDate;
    public const string ResidentialAddress = PersonResolutionContractCatalog.ResidentialAddress;
    public const string ConfidentialShelterAddress = PersonResolutionContractCatalog.ConfidentialShelterAddress;
    public const string TerritorialReference = PersonResolutionContractCatalog.TerritorialReference;
    public const string ContactPhone = PersonResolutionContractCatalog.ContactPhone;
    public const string ContactEmail = PersonResolutionContractCatalog.ContactEmail;
    public const string SocialName = PersonResolutionContractCatalog.SocialName;

    private static readonly ResolutionSourceField[] Fields = PersonResolutionContractCatalog.All
        .Select(static field => new ResolutionSourceField(
            field.Code,
            ToWorkerSemantic(field.Semantic),
            field.CompatibilityProfile,
            field.EligibleForResolution,
            field.MultiValued))
        .ToArray();

    private static readonly IReadOnlyDictionary<string, ResolutionSourceField> ByCode = Fields
        .ToDictionary(static field => field.CanonicalCode, StringComparer.Ordinal);

    public static IReadOnlyList<ResolutionSourceField> All => Fields;

    public static IReadOnlyList<ResolutionSourceField> Eligible => Fields
        .Where(static field => field.EligibleForResolution)
        .OrderBy(static field => field.CanonicalCode, StringComparer.Ordinal)
        .ToArray();

    public static IReadOnlyList<ResolutionSourceField> EligibleTransversal => Eligible
        .Where(static field => field.CompatibilityProfile is null)
        .ToArray();

    public static bool TryGet(string code, out ResolutionSourceField field)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            field = null!;
            return false;
        }

        return ByCode.TryGetValue(ResolutionSourceField.Canonicalize(code), out field!);
    }

    public static bool IsEligible(string code) =>
        TryGet(code, out var field) && field.EligibleForResolution;

    private static ResolutionAttributeSemantic ToWorkerSemantic(PersonResolutionSemantic semantic) => semantic switch
    {
        PersonResolutionSemantic.PersonName => ResolutionAttributeSemantic.PersonName,
        PersonResolutionSemantic.Date => ResolutionAttributeSemantic.Date,
        PersonResolutionSemantic.Phone => ResolutionAttributeSemantic.Phone,
        PersonResolutionSemantic.Email => ResolutionAttributeSemantic.Email,
        PersonResolutionSemantic.Address => ResolutionAttributeSemantic.Address,
        PersonResolutionSemantic.Text => ResolutionAttributeSemantic.Text,
        PersonResolutionSemantic.Categorical => ResolutionAttributeSemantic.Categorical,
        _ => throw new ArgumentOutOfRangeException(nameof(semantic), semantic, null)
    };
}
