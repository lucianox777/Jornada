using System.Globalization;
using System.Text;

namespace Jornada.Contracts;

/// <summary>
/// Projeções básicas, determinísticas e versionadas para nomes de Pessoa em PT-BR.
/// Nenhuma projeção substitui o valor original. As etapas são deliberadamente separadas
/// para que o Calibrador consiga medir o poder discriminante de cada representação.
/// </summary>
public sealed record PersonNameBasicProjection(
    string Upper,
    string UpperNoDiacritics,
    string WithoutPortugueseParticles);

public static class PersonNameBasicNormalization
{
    public const string MethodVersion = "PERSON_NAME_BASIC_PTBR_V1";

    private static readonly HashSet<string> PortugueseParticles = new(StringComparer.Ordinal)
    {
        "DA", "DAS", "DE", "DO", "DOS"
    };

    public static PersonNameBasicProjection? Project(string? value)
    {
        var upper = UpperAndCollapseWhitespace(value);
        if (upper is null)
            return null;

        var noDiacritics = RemoveDiacritics(upper);
        var withoutParticles = RemovePortugueseParticles(noDiacritics);
        return new PersonNameBasicProjection(upper, noDiacritics, withoutParticles);
    }

    private static string? UpperAndCollapseWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var upper = value.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        var normalized = new StringBuilder(upper.Length);
        var pendingSpace = false;

        foreach (var character in upper)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = normalized.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                normalized.Append(' ');
                pendingSpace = false;
            }

            normalized.Append(character);
        }

        return normalized.Length == 0 ? null : normalized.ToString();
    }

    private static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var withoutDiacritics = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                withoutDiacritics.Append(character);
        }

        return withoutDiacritics.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string RemovePortugueseParticles(string value)
    {
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var retained = tokens.Where(token => !PortugueseParticles.Contains(token)).ToArray();

        // Uma derivação nunca deve transformar um valor não vazio em chave vazia.
        return retained.Length == 0 ? value : string.Join(' ', retained);
    }
}
