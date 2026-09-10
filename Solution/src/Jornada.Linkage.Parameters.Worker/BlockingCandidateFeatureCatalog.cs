using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Vocabulário canônico de atributos que o otimizador deve considerar no espaço de busca do blocking
/// probabilístico, executado somente quando não há CPF válido para resolução determinística.
/// A presença no catálogo não promove o atributo automaticamente para a política operacional.
/// </summary>
public static class BlockingCandidateFeatureCatalog
{
    public const string FullName = BlockingFeatureNames.FullName;
    public const string FirstName = BlockingFeatureNames.FirstName;
    public const string Surnames = BlockingFeatureNames.Surnames;
    public const string LastName = BlockingFeatureNames.LastName;
    public const string MotherFullName = BlockingFeatureNames.MotherFullName;
    public const string MotherFirstName = BlockingFeatureNames.MotherFirstName;
    public const string MotherSurnames = BlockingFeatureNames.MotherSurnames;
    public const string MotherLastName = BlockingFeatureNames.MotherLastName;
    public const string BirthDay = BlockingFeatureNames.BirthDay;
    public const string BirthMonth = BlockingFeatureNames.BirthMonth;
    public const string BirthYear = BlockingFeatureNames.BirthYear;

    public static IReadOnlyList<string> RequiredOptimizerCandidates { get; } =
        new[]
        {
            FullName,
            FirstName,
            Surnames,
            LastName,
            MotherFullName,
            MotherFirstName,
            MotherSurnames,
            MotherLastName,
            BirthDay,
            BirthMonth,
            BirthYear
        };

    public static bool IsNameFeature(string field) =>
        string.Equals(field, FullName, StringComparison.Ordinal) ||
        string.Equals(field, FirstName, StringComparison.Ordinal) ||
        string.Equals(field, Surnames, StringComparison.Ordinal) ||
        string.Equals(field, LastName, StringComparison.Ordinal) ||
        string.Equals(field, MotherFullName, StringComparison.Ordinal) ||
        string.Equals(field, MotherFirstName, StringComparison.Ordinal) ||
        string.Equals(field, MotherSurnames, StringComparison.Ordinal) ||
        string.Equals(field, MotherLastName, StringComparison.Ordinal);

    public static bool IsMotherNameFeature(string field) =>
        string.Equals(field, MotherFullName, StringComparison.Ordinal) ||
        string.Equals(field, MotherFirstName, StringComparison.Ordinal) ||
        string.Equals(field, MotherSurnames, StringComparison.Ordinal) ||
        string.Equals(field, MotherLastName, StringComparison.Ordinal);

    public static bool IsBirthDateComponent(string field) =>
        string.Equals(field, BirthDay, StringComparison.Ordinal) ||
        string.Equals(field, BirthMonth, StringComparison.Ordinal) ||
        string.Equals(field, BirthYear, StringComparison.Ordinal);
}
