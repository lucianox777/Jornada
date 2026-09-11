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

/// <summary>
/// Adaptador de compatibilidade para o planner introduzido na primeira fatia. A fonte de
/// verdade dos algoritmos e de suas colunas passou a ser HomologatedResolutionAlgorithmCatalog.
/// </summary>
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
/// Artefato gerado pelo Calibrador a partir dos atributos originais e dos algoritmos
/// homologados. Ele descreve o vocabulário de projeções disponível. O BLOCKING_PLAN é outro
/// artefato e apenas seleciona/combina features desta projeção.
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
/// Fachada de compatibilidade que expõe os algoritmos homologados agrupados por semântica.
/// Diferente da versão inicial, não existe mais a premissa de um único algoritmo por tipo de
/// atributo: novos algoritmos podem ser registrados sem alterar o planner.
/// </summary>
public static class HomologatedResolutionModelCatalog
{
    public const string CatalogVersion = HomologatedResolutionAlgorithmCatalog.CatalogVersion;

    private static readonly IReadOnlyDictionary<ResolutionAttributeSemantic, HomologatedResolutionModel> Models =
        HomologatedResolutionAlgorithmCatalog.All
            .GroupBy(static algorithm => algorithm.Semantic)
            .ToDictionary(
                static group => group.Key,
                static group => new HomologatedResolutionModel(
                    $"{group.Key.ToString().ToUpperInvariant()}_ALGORITHM_SET",
                    CatalogVersion,
                    group.Key,
                    group
                        .OrderBy(static algorithm => algorithm.QualifiedAlgorithm, StringComparer.Ordinal)
                        .SelectMany(static algorithm => algorithm.OutputColumns.Select(column =>
                            new HomologatedResolutionTransformation(
                                algorithm.Algorithm,
                                algorithm.AlgorithmVersion,
                                column.CanonicalOutputSuffix,
                                column.Materialization,
                                column.MultiValued,
                                column.CandidateForBlocking)))
                        .ToArray()));

    public static IReadOnlyCollection<HomologatedResolutionModel> All => Models.Values.ToArray();

    public static bool TryGet(ResolutionAttributeSemantic semantic, out HomologatedResolutionModel model) =>
        Models.TryGetValue(semantic, out model!);
}

/// <summary>
/// Gera automaticamente o esquema de projeções a partir dos atributos originais. O catálogo
/// define apenas algoritmos já implementados/homologados e suas colunas fixas por versão;
/// a existência no catálogo não promove a feature para o BLOCKING_PLAN. O Calibrador mede
/// seu valor antes da publicação operacional.
/// </summary>
public static class ResolutionProjectionPlanner
{
    public const string PlannerVersion = "RESOLUTION_PROJECTION_PLANNER_V2";

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
            .ThenBy(static feature => feature.Algorithm, StringComparer.Ordinal)
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
        var suffix = ResolutionSourceAttribute.Canonicalize(transformation.OutputSuffix);

        if (profile == "PERSON_NAME")
        {
            return suffix switch
            {
                "normalized" => "name_full",
                "first" => "name_first",
                "surnames" => "name_surnames",
                "last" => "name_last",
                _ => GenericFeatureName(source, transformation)
            };
        }

        if (profile == "MOTHER_NAME")
        {
            return suffix switch
            {
                "normalized" => "mother_name_full",
                "first" => "mother_name_first",
                "surnames" => "mother_name_surnames",
                "last" => "mother_name_last",
                _ => GenericFeatureName(source, transformation)
            };
        }

        if (profile == "BIRTH_DATE")
        {
            return suffix switch
            {
                "day" => "birth_day",
                "month" => "birth_month",
                "year" => "birth_year",
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
