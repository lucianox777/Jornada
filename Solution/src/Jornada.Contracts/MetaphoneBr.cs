using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jornada.Contracts;

/// <summary>
/// Projeção fonética determinística para nomes brasileiros.
///
/// A sequência de regras é uma implementação C# compatível com o algoritmo público
/// metaphonebr do Ipea, congelado para esta versão na referência upstream abaixo.
/// A projeção é adicional: nunca substitui o nome original ou outras normalizações.
///
/// Upstream: ipeadata-lab/metaphonebr 0.0.5
/// Commit de referência: 17fdee95581442cdcc98fddc30aea3079caf27ae
/// Licença upstream: MIT; copyright Ipea, 2025.
/// </summary>
public static partial class MetaphoneBr
{
    public const string MethodVersion = "PERSON_NAME_METAPHONE_BR_V1";
    public const string UpstreamPackageVersion = "0.0.5";
    public const string UpstreamCommit = "17fdee95581442cdcc98fddc30aea3079caf27ae";

    public static string? Encode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var phonetic = Preprocess(value);
        if (phonetic.Length == 0)
            return null;

        // H inicial é mudo no modelo de referência.
        phonetic = InitialHRegex().Replace(phonetic, string.Empty);

        // Dígrafos: preservar a ordem do algoritmo upstream congelado.
        phonetic = phonetic.Replace("LH", "1", StringComparison.Ordinal);
        phonetic = phonetic.Replace("NH", "3", StringComparison.Ordinal);
        phonetic = phonetic.Replace("CH", "X", StringComparison.Ordinal);
        phonetic = phonetic.Replace("SH", "X", StringComparison.Ordinal);
        phonetic = phonetic.Replace("SCH", "X", StringComparison.Ordinal);
        phonetic = phonetic.Replace("PH", "F", StringComparison.Ordinal);
        phonetic = ScBeforeEiRegex().Replace(phonetic, "S");
        phonetic = ScBeforeAouRegex().Replace(phonetic, "SK");
        phonetic = QuBeforeEiRegex().Replace(phonetic, "K");
        phonetic = phonetic.Replace("QU", "K", StringComparison.Ordinal);

        // Consoantes de som aproximado no português brasileiro.
        phonetic = CBeforeEiRegex().Replace(phonetic, "S");
        phonetic = COtherwiseRegex().Replace(phonetic, "K");
        phonetic = phonetic.Replace("C", "K", StringComparison.Ordinal);
        phonetic = GBeforeEiRegex().Replace(phonetic, "J");
        phonetic = phonetic.Replace("Q", "K", StringComparison.Ordinal);
        phonetic = phonetic.Replace("W", "V", StringComparison.Ordinal);
        phonetic = phonetic.Replace("Y", "I", StringComparison.Ordinal);
        phonetic = phonetic.Replace("Z", "S", StringComparison.Ordinal);

        // Nasal final e compressão de repetições.
        phonetic = FinalNRegex().Replace(phonetic, "M");
        phonetic = DuplicateVowelsRegex().Replace(phonetic, "$1");
        phonetic = DuplicateWordCharactersRegex().Replace(phonetic, "$1");
        phonetic = SpacesRegex().Replace(phonetic, " ").Trim();

        return phonetic.Length == 0 ? null : phonetic;
    }

    private static string Preprocess(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var withoutDiacritics = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                withoutDiacritics.Append(character);
        }

        var upper = withoutDiacritics
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .ToUpperInvariant();

        var lettersAndSpaces = NonLettersRegex().Replace(upper, string.Empty);
        return SpacesRegex().Replace(lettersAndSpaces, " ").Trim();
    }

    [GeneratedRegex(@"\bH", RegexOptions.CultureInvariant)]
    private static partial Regex InitialHRegex();

    [GeneratedRegex(@"SC(?=[EI])", RegexOptions.CultureInvariant)]
    private static partial Regex ScBeforeEiRegex();

    [GeneratedRegex(@"SC(?=[AOU])", RegexOptions.CultureInvariant)]
    private static partial Regex ScBeforeAouRegex();

    [GeneratedRegex(@"QU(?=[EI])", RegexOptions.CultureInvariant)]
    private static partial Regex QuBeforeEiRegex();

    [GeneratedRegex(@"C(?=[EI])", RegexOptions.CultureInvariant)]
    private static partial Regex CBeforeEiRegex();

    [GeneratedRegex(@"C(?![EIH])", RegexOptions.CultureInvariant)]
    private static partial Regex COtherwiseRegex();

    [GeneratedRegex(@"G(?=[EI])", RegexOptions.CultureInvariant)]
    private static partial Regex GBeforeEiRegex();

    [GeneratedRegex(@"N\b", RegexOptions.CultureInvariant)]
    private static partial Regex FinalNRegex();

    [GeneratedRegex(@"([AEIOU])\1+", RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateVowelsRegex();

    [GeneratedRegex(@"(\w)\1+", RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateWordCharactersRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SpacesRegex();

    [GeneratedRegex(@"[^A-Z ]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonLettersRegex();
}
