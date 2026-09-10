namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Vocabulário canônico de atributos que o otimizador deve considerar no espaço de busca do blocking
/// probabilístico, executado somente quando não há CPF válido para resolução determinística.
/// A presença no catálogo não promove o atributo automaticamente para a política operacional.
/// </summary>
public static class BlockingCandidateFeatureCatalog
{
    public const string FullName = "name_full";
    public const string FirstName = "name_first";
    public const string Surnames = "name_surnames";
    public const string LastName = "name_last";
    public const string MotherFullName = "mother_name_full";
    public const string MotherFirstName = "mother_name_first";
    public const string MotherSurnames = "mother_name_surnames";
    public const string MotherLastName = "mother_name_last";
    public const string BirthDay = "birth_day";
    public const string BirthMonth = "birth_month";
    public const string BirthYear = "birth_year";

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
