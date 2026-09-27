using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Linkage.Evaluation;

/// <summary>External synthetic-only exchange; independent Python runner is NOT a Jornada dependency.</summary>
public static class IbgePublicMarginalsExchange
{
    public const string Schema = "JORNADA_IBGE_PUBLIC_MARGINALS_V1";
    public const string Reference = "CENSO2022_NOMES_BRASIL_V1";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Serialize(string referenceCode, string referenceSha256,
        string firstNameSex, IEnumerable<IbgeTypedNameFrequencyEntry> entries)
    {
        if (referenceCode != Reference || referenceSha256 is null ||
            referenceSha256.Length != 64 ||
            referenceSha256.Any(c => !Uri.IsHexDigit(c)) ||
            firstNameSex is not ("TODOS" or "FEMININO"))
            throw new InvalidDataException("Invalid public IBGE marginal provenance.");
        ArgumentNullException.ThrowIfNull(entries);
        var rows = entries.ToArray();
        object[] Project(IbgeNameStatisticKind kind) =>
            rows.Where(x => x.StatisticKind == kind)
                .GroupBy(x => x.Name.Trim().ToUpperInvariant(), StringComparer.Ordinal)
                .Select(g => new { name = g.Key, occurrences = checked(g.Sum(x => x.Occurrences)) })
                .OrderBy(x => x.name, StringComparer.Ordinal)
                .Cast<object>().ToArray();
        if (rows.Any(x => x.Name is null || x.Name.Trim().Length == 0 ||
                          x.Occurrences <= 0 ||
                          x.StatisticKind is not (IbgeNameStatisticKind.FirstName or IbgeNameStatisticKind.Surname)))
            throw new InvalidDataException("Invalid public IBGE marginal row.");
        var first = Project(IbgeNameStatisticKind.FirstName);
        var surnames = Project(IbgeNameStatisticKind.Surname);
        if (first.Length == 0 || surnames.Length == 0)
            throw new InvalidDataException("Both public first-name and surname marginals are required.");
        var document = new
        {
            schema_version = Schema,
            reference_code = referenceCode,
            reference_content_sha256 = referenceSha256.ToLowerInvariant(),
            first_name_sex = firstNameSex,
            surname_sex = "TODOS",
            first_names = first,
            surnames
        };
        return JsonSerializer.Serialize(document, JsonOptions) + "\n";
    }
}
