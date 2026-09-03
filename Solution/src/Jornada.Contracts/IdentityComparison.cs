using System.Globalization;
using System.Text;

namespace Jornada.Contracts;

/// <summary>
/// Regras puras e versionáveis de normalização/comparação usadas tanto na geração
/// dos parâmetros quanto no score probabilístico. Não aplica fonética, remoção de
/// partículas, correção ortográfica nem reordenação de tokens.
/// </summary>
public static class IdentityComparison
{
    public const string NormalizationVersion = "IDENTITY_NORMALIZATION_V1";

    public static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
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

    public static NameComparisonState CompareName(string? left, string? right)
    {
        var a = NormalizeText(left);
        var b = NormalizeText(right);

        if (a is null || b is null)
            return NameComparisonState.LOW;

        if (string.Equals(a, b, StringComparison.Ordinal))
            return NameComparisonState.EXACT;

        var similarity = JaroWinkler(a, b);
        if (similarity >= 0.92d) return NameComparisonState.HIGH;
        if (similarity >= 0.80d) return NameComparisonState.MEDIUM;
        return NameComparisonState.LOW;
    }

    public static double JaroWinkler(string left, string right)
    {
        if (left == right) return 1d;
        if (left.Length == 0 || right.Length == 0) return 0d;

        var matchDistance = Math.Max(left.Length, right.Length) / 2 - 1;
        if (matchDistance < 0) matchDistance = 0;

        var leftMatches = new bool[left.Length];
        var rightMatches = new bool[right.Length];
        var matches = 0;

        for (var i = 0; i < left.Length; i++)
        {
            var start = Math.Max(0, i - matchDistance);
            var end = Math.Min(i + matchDistance + 1, right.Length);

            for (var j = start; j < end; j++)
            {
                if (rightMatches[j] || left[i] != right[j]) continue;
                leftMatches[i] = true;
                rightMatches[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0) return 0d;

        var transpositions = 0;
        var k = 0;
        for (var i = 0; i < left.Length; i++)
        {
            if (!leftMatches[i]) continue;
            while (!rightMatches[k]) k++;
            if (left[i] != right[k]) transpositions++;
            k++;
        }

        var m = (double)matches;
        var jaro = (m / left.Length + m / right.Length + (m - transpositions / 2d) / m) / 3d;

        var prefix = 0;
        var maxPrefix = Math.Min(4, Math.Min(left.Length, right.Length));
        while (prefix < maxPrefix && left[prefix] == right[prefix]) prefix++;

        return jaro + prefix * 0.1d * (1d - jaro);
    }
}

public enum NameComparisonState
{
    EXACT,
    HIGH,
    MEDIUM,
    LOW
}
