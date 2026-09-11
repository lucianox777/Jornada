namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Coluna calculada produzida por uma versão específica de algoritmo de resolução.
/// O conjunto de colunas é parte do contrato imutável de algoritmo@versão: adicionar,
/// remover ou alterar uma coluna exige uma nova versão do algoritmo.
/// </summary>
public sealed record ResolutionAlgorithmOutputColumn(
    string Code,
    string OutputSuffix,
    ResolutionMaterializationKind Materialization,
    bool MultiValued = false,
    bool CandidateForBlocking = true)
{
    public string CanonicalCode => ResolutionSourceAttribute.Canonicalize(Code);
    public string CanonicalOutputSuffix => ResolutionSourceAttribute.Canonicalize(OutputSuffix);
}

/// <summary>
/// Algoritmo homologado para produzir derivações de atributos de Pessoa.
/// Somente implementações existentes e cobertas por teste entram neste catálogo corrente.
/// ImplementationReferences e TestReferences tornam a homologação auditável; OutputColumns
/// fixa o schema produzido por algoritmo@versão.
/// </summary>
public sealed record HomologatedResolutionAlgorithm(
    string Algorithm,
    string AlgorithmVersion,
    ResolutionAttributeSemantic Semantic,
    IReadOnlyList<ResolutionAlgorithmOutputColumn> OutputColumns,
    IReadOnlyList<string> ImplementationReferences,
    IReadOnlyList<string> TestReferences)
{
    public string QualifiedAlgorithm => $"{Algorithm}@{AlgorithmVersion}";
}

/// <summary>
/// Catálogo extensível de algoritmos homologados. Pode haver vários algoritmos para a mesma
/// semântica de atributo. A extensão ocorre pela inclusão de nova definição versionada;
/// versões publicadas nunca têm sua lista de OutputColumns alterada silenciosamente.
/// </summary>
public static class HomologatedResolutionAlgorithmCatalog
{
    public const string CatalogVersion = "RESOLUTION_ALGORITHM_CATALOG_V1";

    public const string PersonNameComponentsAlgorithm = "PERSON_NAME_COMPONENTS";
    public const string PersonNameComponentsVersion = "V2";
    public const string DateComponentsAlgorithm = "DATE_COMPONENTS";
    public const string DateComponentsVersion = "V2";
    public const string BrazilianPhoneCanonicalAlgorithm = "TELEFONE_BR_CANONICO";
    public const string BrazilianPhoneCanonicalVersion = "V2";
    public const string EmailCanonicalAlgorithm = "EMAIL_CANONICO";
    public const string EmailCanonicalVersion = "V2";

    private static readonly HomologatedResolutionAlgorithm[] Algorithms =
    {
        new(
            PersonNameComponentsAlgorithm,
            PersonNameComponentsVersion,
            ResolutionAttributeSemantic.PersonName,
            new ResolutionAlgorithmOutputColumn[]
            {
                new("normalized", "normalized", ResolutionMaterializationKind.ProcessorMaterialized),
                new("first", "first", ResolutionMaterializationKind.ProcessorMaterialized),
                new("surnames", "surnames", ResolutionMaterializationKind.MultiValuedProjection, MultiValued: true),
                new("last", "last", ResolutionMaterializationKind.ProcessorMaterialized)
            },
            new[]
            {
                "Solution/src/Jornada.Contracts/BlockingProjectionKeys.cs#BlockingProjectionKeyProjector@BLOCKING_PROJECTION_KEY_PROJECTOR_V2",
                "Solution/src/Jornada.Contracts/IdentityComparison.cs#NormalizeText"
            },
            new[]
            {
                "Solution/tests/Jornada.Tests/BlockingProjectionKeyProjectorTests.cs"
            }),

        new(
            DateComponentsAlgorithm,
            DateComponentsVersion,
            ResolutionAttributeSemantic.Date,
            new ResolutionAlgorithmOutputColumn[]
            {
                new("day", "day", ResolutionMaterializationKind.GeneratedColumn),
                new("month", "month", ResolutionMaterializationKind.GeneratedColumn),
                new("year", "year", ResolutionMaterializationKind.GeneratedColumn)
            },
            new[]
            {
                "Solution/src/Jornada.Contracts/BlockingProjectionKeys.cs#BlockingProjectionKeyProjector@BLOCKING_PROJECTION_KEY_PROJECTOR_V2"
            },
            new[]
            {
                "Solution/tests/Jornada.Tests/BlockingProjectionKeyProjectorTests.cs"
            }),

        new(
            BrazilianPhoneCanonicalAlgorithm,
            BrazilianPhoneCanonicalVersion,
            ResolutionAttributeSemantic.Phone,
            new ResolutionAlgorithmOutputColumn[]
            {
                new("canonical", "canonical", ResolutionMaterializationKind.ProcessorMaterialized)
            },
            new[]
            {
                "Solution/src/Jornada.Processor.Worker/TransversalAttributeInstanceKey.cs#TELEFONE_BR_CANONICO_V2",
                "Solution/database/Jornada_Fase1.sql#ref.fn_telefone_br_canonico_v2"
            },
            new[]
            {
                "Solution/tests/Jornada.Tests/Unit/TransversalAttributeInstanceKeyTests.cs",
                "Solution/tests/Jornada.Tests/Unit/DeterministicPropertyTests.cs",
                "Solution/tests/Jornada.Integration.Tests/Integration/PhoneNormalizationConformanceTests.cs",
                "Solution/tests/fixtures/phone/telefone-br-canonico-v2.json"
            }),

        new(
            EmailCanonicalAlgorithm,
            EmailCanonicalVersion,
            ResolutionAttributeSemantic.Email,
            new ResolutionAlgorithmOutputColumn[]
            {
                new("canonical", "canonical", ResolutionMaterializationKind.ProcessorMaterialized)
            },
            new[]
            {
                "Solution/src/Jornada.Processor.Worker/TransversalAttributeInstanceKey.cs#EMAIL_CANONICO_V2",
                "Solution/database/Jornada_Fase1.sql#ref.fn_email_canonico_v2"
            },
            new[]
            {
                "Solution/tests/Jornada.Tests/Unit/TransversalAttributeInstanceKeyTests.cs",
                "Solution/tests/Jornada.Tests/Unit/DeterministicPropertyTests.cs",
                "Solution/tests/Jornada.Integration.Tests/Integration/EmailNormalizationConformanceTests.cs",
                "Solution/tests/fixtures/email/email-canonico-v2.json"
            })
    };

    static HomologatedResolutionAlgorithmCatalog()
    {
        Validate(Algorithms);
    }

    public static IReadOnlyList<HomologatedResolutionAlgorithm> All => Algorithms;

    public static IReadOnlyList<HomologatedResolutionAlgorithm> ForSemantic(ResolutionAttributeSemantic semantic) =>
        Algorithms
            .Where(algorithm => algorithm.Semantic == semantic)
            .OrderBy(static algorithm => algorithm.QualifiedAlgorithm, StringComparer.Ordinal)
            .ToArray();

    public static bool TryGet(string algorithm, string version, out HomologatedResolutionAlgorithm definition)
    {
        var found = Algorithms.SingleOrDefault(candidate =>
            string.Equals(candidate.Algorithm, algorithm, StringComparison.Ordinal) &&
            string.Equals(candidate.AlgorithmVersion, version, StringComparison.Ordinal));
        if (found is null)
        {
            definition = null!;
            return false;
        }

        definition = found;
        return true;
    }

    private static void Validate(IEnumerable<HomologatedResolutionAlgorithm> algorithms)
    {
        var qualified = new HashSet<string>(StringComparer.Ordinal);
        foreach (var algorithm in algorithms)
        {
            if (string.IsNullOrWhiteSpace(algorithm.Algorithm) || string.IsNullOrWhiteSpace(algorithm.AlgorithmVersion))
                throw new InvalidOperationException("Algoritmo homologado deve possuir código e versão.");
            if (!qualified.Add(algorithm.QualifiedAlgorithm))
                throw new InvalidOperationException($"Algoritmo homologado duplicado: {algorithm.QualifiedAlgorithm}.");
            if (algorithm.OutputColumns.Count == 0)
                throw new InvalidOperationException($"{algorithm.QualifiedAlgorithm} deve declarar ao menos uma coluna de saída.");
            if (algorithm.ImplementationReferences.Count == 0 || algorithm.TestReferences.Count == 0)
                throw new InvalidOperationException($"{algorithm.QualifiedAlgorithm} exige evidência de implementação e teste.");

            var columns = new HashSet<string>(StringComparer.Ordinal);
            var suffixes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var column in algorithm.OutputColumns)
            {
                if (!columns.Add(column.CanonicalCode))
                    throw new InvalidOperationException($"Coluna duplicada em {algorithm.QualifiedAlgorithm}: {column.CanonicalCode}.");
                if (!suffixes.Add(column.CanonicalOutputSuffix))
                    throw new InvalidOperationException($"Sufixo duplicado em {algorithm.QualifiedAlgorithm}: {column.CanonicalOutputSuffix}.");
            }
        }
    }
}
