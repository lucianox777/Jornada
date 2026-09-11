using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Semântica declarada do atributo original recebido pela Jornada. O dado original continua
/// sendo verdade de origem; qualquer representação produzida a partir dele é calculada.
/// </summary>
public enum ResolutionAttributeSemantic
{
    PersonName,
    Date,
    Phone,
    Email,
    Address,
    Text,
    Categorical
}

public enum ResolutionFeatureOrigin
{
    Original,
    Calculated
}

/// <summary>
/// Sugestão física da derivação. O Calibrador pode promover uma derivação sem acoplar a
/// semântica do blocking a um SGBD específico. GeneratedColumn representa uma expressão
/// determinística que pode ser implementada como computed/generated column quando o provider
/// suportar a expressão; ProcessorMaterialized mantém a mesma semântica por materialização.
/// MultiValuedProjection é reservada para derivações que produzem mais de um valor por Pessoa.
/// </summary>
public enum ResolutionMaterializationKind
{
    Source,
    GeneratedColumn,
    ProcessorMaterialized,
    MultiValuedProjection
}

public sealed record ResolutionSourceAttribute(
    string Code,
    ResolutionAttributeSemantic Semantic,
    string? CompatibilityProfile = null)
{
    public string CanonicalCode => Canonicalize(Code);

    internal static string Canonicalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("O código do atributo é obrigatório.", nameof(value));

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
                builder.Append(ch);
            else if (builder.Length > 0 && builder[builder.Length - 1] != '_')
                builder.Append('_');
        }

        var result = builder.ToString().Trim('_');
        if (result.Length == 0)
            throw new ArgumentException("O código do atributo não produz identificador canônico.", nameof(value));
        return result;
    }
}

public sealed record HomologatedResolutionTransformation(
    string Algorithm,
    string AlgorithmVersion,
    string OutputSuffix,
    ResolutionMaterializationKind Materialization,
    bool MultiValued = false,
    bool CandidateForBlocking = true)
{
    public string QualifiedAlgorithm => $"{Algorithm}@{AlgorithmVersion}";
}

public sealed record HomologatedResolutionModel(
    string Model,
    string ModelVersion,
    ResolutionAttributeSemantic Semantic,
    IReadOnlyList<HomologatedResolutionTransformation> Transformations)
{
    public string QualifiedModel => $"{Model}@{ModelVersion}";
}

public sealed record ResolutionProjectedFeature(
    string Feature,
    string SourceAttribute,
    ResolutionAttributeSemantic SourceSemantic,
    ResolutionFeatureOrigin Origin,
    string? ResolutionModel,
    string? Algorithm,
    ResolutionMaterializationKind Materialization,
    bool MultiValued,
    bool CandidateForBlocking);

/// <summary>
/// Artefato gerado pelo Calibrador a partir dos atributos originais e dos modelos homologados.
/// Ele descreve o vocabulário de projeções disponível. O BLOCKING_PLAN é outro artefato e
/// apenas seleciona/combina features desta projeção.
/// </summary>
public sealed record ResolutionProjectionPlan(
    string SchemaVersion,
    string CatalogVersion,
    IReadOnlyList<ResolutionSourceAttribute> Sources,
    IReadOnlyList<ResolutionProjectedFeature> Features,
    string Fingerprint)
{
    public IReadOnlyList<string> BlockingCandidateFeatures => Features
        .Where(static feature => feature.CandidateForBlocking)
        .Select(static feature => feature.Feature)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(static feature => feature, StringComparer.Ordinal)
        .ToArray();
}

/// <summary>
/// Catálogo pequeno e governado de modelos/algoritmos homologados. Estar no catálogo apenas
/// autoriza a experimentação. Poder discriminante, combinações e promoção física são decisões
/// do Calibrador sobre o corpus observado.
///
/// Esta V1 contém somente transformações já existentes no código da Jornada: normalização
/// canônica de nomes, decomposição em primeiro/sobrenomes/último nome e componentes de data.
/// Não introduz algoritmo fonético ou similaridade novo.
/// </summary>
public static class HomologatedResolutionModelCatalog
{
    public const string CatalogVersion = "RESOLUTION_MODEL_CATALOG_V1";

    public const string PersonNameModel = "PERSON_NAME_COMPONENTS";
    public const string DateComponentsModel = "DATE_COMPONENTS";

    public const string CanonicalNameAlgorithm = "IDENTITY_TEXT_CANONICAL";
    public const string NameFirstAlgorithm = "NAME_FIRST_TOKEN";
    public const string NameSurnamesAlgorithm = "NAME_SURNAME_TOKENS";
    public const string NameLastAlgorithm = "NAME_LAST_TOKEN";
    public const string DateDayAlgorithm = "DATE_DAY";
    public const string DateMonthAlgorithm = "DATE_MONTH";
    public const string DateYearAlgorithm = "DATE_YEAR";

    private static readonly IReadOnlyDictionary<ResolutionAttributeSemantic, HomologatedResolutionModel> Models =
        new Dictionary<ResolutionAttributeSemantic, HomologatedResolutionModel>
        {
            [ResolutionAttributeSemantic.PersonName] = new(
                PersonNameModel,
                "V1",
                ResolutionAttributeSemantic.PersonName,
                new HomologatedResolutionTransformation[]
                {
                    new(CanonicalNameAlgorithm, "V1", "normalized", ResolutionMaterializationKind.ProcessorMaterialized),
                    new(NameFirstAlgorithm, "V1", "first", ResolutionMaterializationKind.ProcessorMaterialized),
                    new(NameSurnamesAlgorithm, "V1", "surnames", ResolutionMaterializationKind.MultiValuedProjection, MultiValued: true),
                    new(NameLastAlgorithm, "V1", "last", ResolutionMaterializationKind.ProcessorMaterialized)
                }),
            [ResolutionAttributeSemantic.Date] = new(
                DateComponentsModel,
                "V1",
                ResolutionAttributeSemantic.Date,
                new HomologatedResolutionTransformation[]
                {
                    new(DateDayAlgorithm, "V1", "day", ResolutionMaterializationKind.GeneratedColumn),
                    new(DateMonthAlgorithm, "V1", "month", ResolutionMaterializationKind.GeneratedColumn),
                    new(DateYearAlgorithm, "V1", "year", ResolutionMaterializationKind.GeneratedColumn)
                })
        };

    public static IReadOnlyCollection<HomologatedResolutionModel> All => Models.Values.ToArray();

    public static bool TryGet(ResolutionAttributeSemantic semantic, out HomologatedResolutionModel model) =>
        Models.TryGetValue(semantic, out model!);
}

/// <summary>
/// Gera automaticamente o esquema de projeções a partir dos atributos originais. O catálogo
/// define apenas técnicas homologadas; a existência de uma técnica não a promove para o
/// BLOCKING_PLAN. O Calibrador mede seu valor antes da publicação operacional.
/// </summary>
public static class ResolutionProjectionPlanner
{
    public const string PlannerVersion = "RESOLUTION_PROJECTION_PLANNER_V1";

    public static ResolutionProjectionPlan Build(
        IEnumerable<ResolutionSourceAttribute> attributes,
        string schemaVersion)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        if (string.IsNullOrWhiteSpace(schemaVersion))
            throw new ArgumentException("A versão do esquema é obrigatória.", nameof(schemaVersion));

        var sources = attributes
            .Select(static attribute => attribute ?? throw new ArgumentException("Atributo nulo não é permitido."))
            .GroupBy(static attribute => attribute.CanonicalCode, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static attribute => attribute.CanonicalCode, StringComparer.Ordinal)
            .ToArray();

        if (sources.Length == 0)
            throw new ArgumentException("Ao menos um atributo original é obrigatório.", nameof(attributes));

        var features = new List<ResolutionProjectedFeature>();
        foreach (var source in sources)
        {
            features.Add(new ResolutionProjectedFeature(
                $"source__{source.CanonicalCode}",
                source.CanonicalCode,
                source.Semantic,
                ResolutionFeatureOrigin.Original,
                null,
                null,
                ResolutionMaterializationKind.Source,
                MultiValued: false,
                CandidateForBlocking: false));

            if (!HomologatedResolutionModelCatalog.TryGet(source.Semantic, out var model))
                continue;

            foreach (var transformation in model.Transformations)
            {
                var feature = ResolveFeatureName(source, transformation);
                features.Add(new ResolutionProjectedFeature(
                    feature,
                    source.CanonicalCode,
                    source.Semantic,
                    ResolutionFeatureOrigin.Calculated,
                    model.QualifiedModel,
                    transformation.QualifiedAlgorithm,
                    transformation.Materialization,
                    transformation.MultiValued,
                    transformation.CandidateForBlocking));
            }
        }

        var ordered = features
            .OrderBy(static feature => feature.Feature, StringComparer.Ordinal)
            .ThenBy(static feature => feature.SourceAttribute, StringComparer.Ordinal)
            .ToArray();
        var fingerprint = Fingerprint(schemaVersion.Trim(), sources, ordered);

        return new ResolutionProjectionPlan(
            schemaVersion.Trim(),
            HomologatedResolutionModelCatalog.CatalogVersion,
            sources,
            ordered,
            fingerprint);
    }

    private static string ResolveFeatureName(
        ResolutionSourceAttribute source,
        HomologatedResolutionTransformation transformation)
    {
        var profile = source.CompatibilityProfile?.Trim().ToUpperInvariant();
        if (profile == "PERSON_NAME")
        {
            return transformation.Algorithm switch
            {
                HomologatedResolutionModelCatalog.CanonicalNameAlgorithm => "name_full",
                HomologatedResolutionModelCatalog.NameFirstAlgorithm => "name_first",
                HomologatedResolutionModelCatalog.NameSurnamesAlgorithm => "name_surnames",
                HomologatedResolutionModelCatalog.NameLastAlgorithm => "name_last",
                _ => GenericFeatureName(source, transformation)
            };
        }

        if (profile == "MOTHER_NAME")
        {
            return transformation.Algorithm switch
            {
                HomologatedResolutionModelCatalog.CanonicalNameAlgorithm => "mother_name_full",
                HomologatedResolutionModelCatalog.NameFirstAlgorithm => "mother_name_first",
                HomologatedResolutionModelCatalog.NameSurnamesAlgorithm => "mother_name_surnames",
                HomologatedResolutionModelCatalog.NameLastAlgorithm => "mother_name_last",
                _ => GenericFeatureName(source, transformation)
            };
        }

        if (profile == "BIRTH_DATE")
        {
            return transformation.Algorithm switch
            {
                HomologatedResolutionModelCatalog.DateDayAlgorithm => "birth_day",
                HomologatedResolutionModelCatalog.DateMonthAlgorithm => "birth_month",
                HomologatedResolutionModelCatalog.DateYearAlgorithm => "birth_year",
                _ => GenericFeatureName(source, transformation)
            };
        }

        return GenericFeatureName(source, transformation);
    }

    private static string GenericFeatureName(
        ResolutionSourceAttribute source,
        HomologatedResolutionTransformation transformation) =>
        $"{source.CanonicalCode}__{ResolutionSourceAttribute.Canonicalize(transformation.OutputSuffix)}";

    private static string Fingerprint(
        string schemaVersion,
        IReadOnlyList<ResolutionSourceAttribute> sources,
        IReadOnlyList<ResolutionProjectedFeature> features)
    {
        var canonical = new StringBuilder()
            .Append(PlannerVersion).Append('|')
            .Append(HomologatedResolutionModelCatalog.CatalogVersion).Append('|')
            .Append(schemaVersion).AppendLine();

        foreach (var source in sources)
            canonical.Append("S|").Append(source.CanonicalCode).Append('|').Append(source.Semantic).Append('|').Append(source.CompatibilityProfile).AppendLine();
        foreach (var feature in features)
            canonical.Append("F|").Append(feature.Feature).Append('|').Append(feature.SourceAttribute).Append('|')
                .Append(feature.Origin).Append('|').Append(feature.ResolutionModel).Append('|').Append(feature.Algorithm).Append('|')
                .Append(feature.Materialization).Append('|').Append(feature.MultiValued).Append('|').Append(feature.CandidateForBlocking).AppendLine();

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }
}
