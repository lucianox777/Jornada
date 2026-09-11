using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Vocabulário canônico de compatibilidade do blocking probabilístico. As features calculadas
/// são geradas pelo ResolutionProjectionPlanner; presença na projeção apenas autoriza avaliação
/// pelo Calibrador e não promove automaticamente a feature para a política operacional.
/// </summary>
public static class BlockingCandidateFeatureCatalog
{
    public const string FullName = BlockingFeatureNames.FullName;
    public const string FullNameUpper = BlockingFeatureNames.FullNameUpper;
    public const string FullNameUpperNoDiacritics = BlockingFeatureNames.FullNameUpperNoDiacritics;
    public const string FullNameWithoutParticles = BlockingFeatureNames.FullNameWithoutParticles;
    public const string FullNamePhoneticPtBr = BlockingFeatureNames.FullNamePhoneticPtBr;
    public const string FirstName = BlockingFeatureNames.FirstName;
    public const string Surnames = BlockingFeatureNames.Surnames;
    public const string LastName = BlockingFeatureNames.LastName;
    public const string MotherFullName = BlockingFeatureNames.MotherFullName;
    public const string MotherFullNameUpper = BlockingFeatureNames.MotherFullNameUpper;
    public const string MotherFullNameUpperNoDiacritics = BlockingFeatureNames.MotherFullNameUpperNoDiacritics;
    public const string MotherFullNameWithoutParticles = BlockingFeatureNames.MotherFullNameWithoutParticles;
    public const string MotherFullNamePhoneticPtBr = BlockingFeatureNames.MotherFullNamePhoneticPtBr;
    public const string MotherFirstName = BlockingFeatureNames.MotherFirstName;
    public const string MotherSurnames = BlockingFeatureNames.MotherSurnames;
    public const string MotherLastName = BlockingFeatureNames.MotherLastName;
    public const string BirthDay = BlockingFeatureNames.BirthDay;
    public const string BirthMonth = BlockingFeatureNames.BirthMonth;
    public const string BirthYear = BlockingFeatureNames.BirthYear;

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

    public static IReadOnlyList<string> RequiredOptimizerCandidates => CalibratorCandidates;

    public static bool IsNameFeature(string field) =>
        string.Equals(field, FullName, StringComparison.Ordinal) ||
        string.Equals(field, FullNameUpper, StringComparison.Ordinal) ||
        string.Equals(field, FullNameUpperNoDiacritics, StringComparison.Ordinal) ||
        string.Equals(field, FullNameWithoutParticles, StringComparison.Ordinal) ||
        string.Equals(field, FullNamePhoneticPtBr, StringComparison.Ordinal) ||
        string.Equals(field, FirstName, StringComparison.Ordinal) ||
        string.Equals(field, Surnames, StringComparison.Ordinal) ||
        string.Equals(field, LastName, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullName, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullNameUpper, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullNameUpperNoDiacritics, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullNameWithoutParticles, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullNamePhoneticPtBr, StringComparison.Ordinal) ||
        string.Equals(field, MotherFirstName, StringComparison.Ordinal) ||
        string.Equals(field, MotherSurnames, StringComparison.Ordinal) ||
        string.Equals(field, MotherLastName, StringComparison.Ordinal);

    public static bool IsMotherNameFeature(string field) =>
        string.Equals(field, MotherFullName, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullNameUpper, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullNameUpperNoDiacritics, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullNameWithoutParticles, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullNamePhoneticPtBr, StringComparison.Ordinal) ||
        string.Equals(field, MotherFirstName, StringComparison.Ordinal) ||
        string.Equals(field, MotherSurnames, StringComparison.Ordinal) ||
        string.Equals(field, MotherLastName, StringComparison.Ordinal);

    public static bool IsBirthDateComponent(string field) =>
        string.Equals(field, BirthDay, StringComparison.Ordinal) ||
        string.Equals(field, BirthMonth, StringComparison.Ordinal) ||
        string.Equals(field, BirthYear, StringComparison.Ordinal);
}
