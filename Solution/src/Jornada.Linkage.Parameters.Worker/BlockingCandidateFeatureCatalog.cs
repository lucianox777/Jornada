using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Vocabulário canônico de compatibilidade do blocking probabilístico.
///
/// As features não são mais tratadas como atributos originais da Pessoa: normalizações,
/// FirstName, Surnames, LastName e componentes de data são derivações calculadas. O espaço
/// corrente é gerado pelo ResolutionProjectionPlanner a partir dos atributos originais e dos
/// algoritmos de resolução homologados. A presença na projeção apenas autoriza avaliação pelo
/// Calibrador; não promove automaticamente a feature para a política operacional.
/// </summary>
public static class BlockingCandidateFeatureCatalog
{
    public const string FullName = BlockingFeatureNames.FullName;
    public const string FullNameUpper = BlockingFeatureNames.FullNameUpper;
    public const string FullNameUpperNoDiacritics = BlockingFeatureNames.FullNameUpperNoDiacritics;
    public const string FullNameWithoutParticles = BlockingFeatureNames.FullNameWithoutParticles;
    public const string FirstName = BlockingFeatureNames.FirstName;
    public const string Surnames = BlockingFeatureNames.Surnames;
    public const string LastName = BlockingFeatureNames.LastName;
    public const string MotherFullName = BlockingFeatureNames.MotherFullName;
    public const string MotherFullNameUpper = BlockingFeatureNames.MotherFullNameUpper;
    public const string MotherFullNameUpperNoDiacritics = BlockingFeatureNames.MotherFullNameUpperNoDiacritics;
    public const string MotherFullNameWithoutParticles = BlockingFeatureNames.MotherFullNameWithoutParticles;
    public const string MotherFirstName = BlockingFeatureNames.MotherFirstName;
    public const string MotherSurnames = BlockingFeatureNames.MotherSurnames;
    public const string MotherLastName = BlockingFeatureNames.MotherLastName;
    public const string BirthDay = BlockingFeatureNames.BirthDay;
    public const string BirthMonth = BlockingFeatureNames.BirthMonth;
    public const string BirthYear = BlockingFeatureNames.BirthYear;

    /// <summary>
    /// Projeção corrente gerada a partir de nome, nome da mãe e data de nascimento. Além do
    /// vocabulário legado, inclui as representações básicas PT-BR homologadas para que o
    /// Calibrador possa medir seu poder discriminante e combiná-las com outras features.
    /// </summary>
    public static ResolutionProjectionPlan CurrentResolutionProjectionPlan { get; } =
        ResolutionProjectionPlanner.Build(
            new ResolutionSourceField[]
            {
                new("nome_completo", ResolutionAttributeSemantic.PersonName, "PERSON_NAME"),
                new("nome_mae", ResolutionAttributeSemantic.PersonName, "MOTHER_NAME"),
                new("data_nascimento", ResolutionAttributeSemantic.Date, "BIRTH_DATE")
            },
            "PERSON_RESOLUTION_PROJECTION_V1");

    public static IReadOnlyList<string> CalibratorCandidates { get; } =
        CurrentResolutionProjectionPlan.BlockingCandidateFeatures;

    // Compatibilidade temporária com chamadas/testes anteriores. Não representa outro componente:
    // o "optimizer" histórico é algoritmo interno do próprio Calibrador.
    public static IReadOnlyList<string> RequiredOptimizerCandidates => CalibratorCandidates;

    public static bool IsNameFeature(string field) =>
        string.Equals(field, FullName, StringComparison.Ordinal) ||
        string.Equals(field, FullNameUpper, StringComparison.Ordinal) ||
        string.Equals(field, FullNameUpperNoDiacritics, StringComparison.Ordinal) ||
        string.Equals(field, FullNameWithoutParticles, StringComparison.Ordinal) ||
        string.Equals(field, FirstName, StringComparison.Ordinal) ||
        string.Equals(field, Surnames, StringComparison.Ordinal) ||
        string.Equals(field, LastName, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullName, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullNameUpper, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullNameUpperNoDiacritics, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullNameWithoutParticles, StringComparison.Ordinal) ||
        string.Equals(field, MotherFirstName, StringComparison.Ordinal) ||
        string.Equals(field, MotherSurnames, StringComparison.Ordinal) ||
        string.Equals(field, MotherLastName, StringComparison.Ordinal);

    public static bool IsMotherNameFeature(string field) =>
        string.Equals(field, MotherFullName, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullNameUpper, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullNameUpperNoDiacritics, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullNameWithoutParticles, StringComparison.Ordinal) ||
        string.Equals(field, MotherFirstName, StringComparison.Ordinal) ||
        string.Equals(field, MotherSurnames, StringComparison.Ordinal) ||
        string.Equals(field, MotherLastName, StringComparison.Ordinal);

    public static bool IsBirthDateComponent(string field) =>
        string.Equals(field, BirthDay, StringComparison.Ordinal) ||
        string.Equals(field, BirthMonth, StringComparison.Ordinal) ||
        string.Equals(field, BirthYear, StringComparison.Ordinal);
}
