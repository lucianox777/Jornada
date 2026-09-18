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
    public const string NameComparisonVersionV1 = "WHOLE_NAME_JARO_WINKLER_V1";
    public const string NameComparisonVersionV2 = "POSITIONAL_TOKEN_MIN_JARO_WINKLER_V2";

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

    /// <summary>
    /// Alias legado. Mantém exatamente a semântica histórica de V1 para que modelos
    /// persistidos não sejam reinterpretados quando novos comparadores forem adicionados.
    /// Novos algoritmos devem escolher explicitamente o contrato nominal.
    /// </summary>
    public static NameComparisonState CompareName(string? left, string? right) =>
        CompareName(left, right, NameComparisonContract.WholeNameJaroWinklerV1);

    public static NameComparisonState CompareName(
        string? left,
        string? right,
        NameComparisonContract contract) =>
        contract switch
        {
            NameComparisonContract.WholeNameJaroWinklerV1 => CompareNameV1(left, right),
            NameComparisonContract.PositionalTokenMinJaroWinklerV2 => CompareNameV2(left, right),
            _ => throw new ArgumentOutOfRangeException(nameof(contract), contract, "Contrato nominal desconhecido.")
        };

    public static NameComparisonState CompareNameV1(string? left, string? right)
    {
        var a = NormalizeText(left);
        var b = NormalizeText(right);
        return ClassifyWholeNameV1(a, b);
    }

    /// <summary>
    /// V2 conserva os mesmos thresholds de V1, mas impede que um token fortemente
    /// divergente seja diluído por uma string longa quase toda igual. Quando as duas
    /// formas têm a mesma quantidade de tokens, a similaridade efetiva é o mínimo
    /// entre o Jaro-Winkler do nome completo e o pior Jaro-Winkler posicional entre
    /// tokens. Com quantidades diferentes, V2 mantém o comportamento V1 até existir
    /// evidência para uma política versionada de inserção/remoção/reordenação.
    ///
    /// O contrato não declara que qualquer token seja "sobrenome IBGE": trata apenas
    /// a estrutura observável da string e não remove partículas.
    /// </summary>
    public static NameComparisonState CompareNameV2(string? left, string? right)
    {
        var a = NormalizeText(left);
        var b = NormalizeText(right);

        if (a is null || b is null)
            return NameComparisonState.LOW;

        if (string.Equals(a, b, StringComparison.Ordinal))
            return NameComparisonState.EXACT;

        var similarity = JaroWinkler(a, b);
        var leftTokens = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rightTokens = b.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (leftTokens.Length == rightTokens.Length && leftTokens.Length > 1)
        {
            var weakestTokenSimilarity = 1d;
            for (var i = 0; i < leftTokens.Length; i++)
                weakestTokenSimilarity = Math.Min(
                    weakestTokenSimilarity,
                    JaroWinkler(leftTokens[i], rightTokens[i]));

            similarity = Math.Min(similarity, weakestTokenSimilarity);
        }

        return ClassifySimilarity(similarity);
    }

    private static NameComparisonState ClassifyWholeNameV1(string? a, string? b)
    {
        if (a is null || b is null)
            return NameComparisonState.LOW;

        if (string.Equals(a, b, StringComparison.Ordinal))
            return NameComparisonState.EXACT;

        return ClassifySimilarity(JaroWinkler(a, b));
    }

    private static NameComparisonState ClassifySimilarity(double similarity)
    {
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

public enum NameComparisonContract
{
    WholeNameJaroWinklerV1,
    PositionalTokenMinJaroWinklerV2
}

public enum NameComparisonState
{
    EXACT,
    HIGH,
    MEDIUM,
    LOW
}
