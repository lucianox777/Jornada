namespace Jornada.Contracts;

/// <summary>
/// Política canônica de ausência/utilização de evidência de Pessoa.
/// Evita listas divergentes entre elegibilidade, blocking e calibração.
/// </summary>
public static class PersonResolutionMissingValuePolicy
{
    public const string Version = "PERSON_RESOLUTION_MISSING_VALUE_POLICY_V1";

    private static readonly string[] Sentinels =
    [
        "NAO INFORMADO", "NÃO INFORMADO", "IGNORADO", "DESCONHECIDO",
        "SEM INFORMACAO", "SEM INFORMAÇÃO", "NULL", "NULO", "N/A", "NA",
        "NAO CONSTA", "NÃO CONSTA", "SEM NOME", "INDEFINIDO"
    ];

    private static readonly HashSet<string> SentinelSet =
        new(Sentinels, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> TextSentinels { get; } = Array.AsReadOnly(Sentinels);

    public static string NormalizeTextPlaceholder(string value) =>
        string.Join(" ", value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static bool IsMissingText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return SentinelSet.Contains(NormalizeTextPlaceholder(value));
    }

    public static bool HasUsableName(string? value)
    {
        if (IsMissingText(value)) return false;
        var normalized = NormalizeTextPlaceholder(value!);
        return normalized.Any(char.IsLetter) && !normalized.Any(char.IsDigit);
    }

    public static bool HasUsableBirthDate(DateTime? date)
    {
        if (!date.HasValue) return false;
        var d = date.Value.Date;
        return d.Year >= 1850
            && d <= DateTime.UtcNow.Date
            && d != new DateTime(1900, 1, 1);
    }
}
