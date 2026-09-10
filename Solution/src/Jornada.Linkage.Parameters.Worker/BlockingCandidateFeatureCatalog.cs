namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Vocabulário canônico de atributos que o otimizador deve considerar no espaço de busca do blocking.
/// A presença no catálogo não promove o atributo automaticamente para a política operacional.
/// </summary>
public static class BlockingCandidateFeatureCatalog
{
    public const string FullName = "name_full";
    public const string FirstName = "name_first";
    public const string Surnames = "name_surnames";
    public const string LastName = "name_last";
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
            BirthDay,
            BirthMonth,
            BirthYear
        };

    public static bool IsNameFeature(string field) =>
        string.Equals(field, FullName, StringComparison.Ordinal) ||
        string.Equals(field, FirstName, StringComparison.Ordinal) ||
        string.Equals(field, Surnames, StringComparison.Ordinal) ||
        string.Equals(field, LastName, StringComparison.Ordinal);

    public static bool IsBirthDateComponent(string field) =>
        string.Equals(field, BirthDay, StringComparison.Ordinal) ||
        string.Equals(field, BirthMonth, StringComparison.Ordinal) ||
        string.Equals(field, BirthYear, StringComparison.Ordinal);
}
