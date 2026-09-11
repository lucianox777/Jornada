namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Catálogo explícito de atributos que podem participar da resolução probabilística.
/// Semântica e elegibilidade são dados de catálogo: nunca são inferidas do nome do campo.
/// A ausência de entrada elegível é fail-closed.
/// </summary>
public static class PersonResolutionAttributeCatalog
{
    public const string CatalogVersion = "PERSON_RESOLUTION_ATTRIBUTE_CATALOG_V1";
    public const string ProjectionSchemaVersion = "PERSON_RESOLUTION_PROJECTION_V2";

    public const string FullName = "nome_completo";
    public const string MotherName = "nome_mae";
    public const string BirthDate = "data_nascimento";
    public const string ResidentialAddress = "ENDERECO_RESIDENCIAL";
    public const string ConfidentialShelterAddress = "ENDERECO_CASA_ABRIGO_SIGILOSA";
    public const string TerritorialReference = "REFERENCIA_TERRITORIAL";
    public const string ContactPhone = "TELEFONE_CONTATO";
    public const string ContactEmail = "EMAIL_CONTATO";
    public const string SocialName = "NOME_SOCIAL";

    private static readonly ResolutionSourceField[] Fields =
    {
        new(FullName, ResolutionAttributeSemantic.PersonName, "PERSON_NAME", EligibleForResolution: true),
        new(MotherName, ResolutionAttributeSemantic.PersonName, "MOTHER_NAME", EligibleForResolution: true),
        new(BirthDate, ResolutionAttributeSemantic.Date, "BIRTH_DATE", EligibleForResolution: true),

        // Endereços e referência territorial não possuem elegibilidade de linkage nesta versão.
        // A casa-abrigo-sigilosa é deliberadamente fail-closed e nunca entra por semelhança nominal.
        new(ResidentialAddress, ResolutionAttributeSemantic.Address, EligibleForResolution: false),
        new(ConfidentialShelterAddress, ResolutionAttributeSemantic.Address, EligibleForResolution: false),
        new(TerritorialReference, ResolutionAttributeSemantic.Categorical, EligibleForResolution: false),

        // Instâncias distintas permanecem distintas no corpus; a projeção agrega conjuntos por atributo.
        new(ContactPhone, ResolutionAttributeSemantic.Phone, EligibleForResolution: true, MultiValued: true),
        new(ContactEmail, ResolutionAttributeSemantic.Email, EligibleForResolution: true, MultiValued: true),
        new(SocialName, ResolutionAttributeSemantic.PersonName, EligibleForResolution: true)
    };

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
}
