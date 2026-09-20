using System.Globalization;
using System.Text;
using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Segunda implementação dos comparadores de entrada usados pelo Linkage.
/// Parte de strings/datas brutas sintéticas e não chama o comparador canônico.
/// Esta classe é somente de conferência; não participa do Runner operacional.
/// </summary>
public static class IndependentRawComparatorConference
{
    public const string MethodVersion = "JORNADA_RAW_COMPARATOR_CONFERENCE_V1";
    public const string Scope =
        "RAW_NORMALIZATION_NAME_V1_V2_AND_BIRTH_SEMANTIC_SYNTHETIC_CONFERENCE";

    public static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var buffer = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                buffer.Append(character);
        }

        var upper = buffer
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .ToUpperInvariant();

        buffer.Clear();
        var addSpace = false;
        foreach (var character in upper)
        {
            if (char.IsWhiteSpace(character))
            {
                addSpace = buffer.Length > 0;
                continue;
            }

            if (addSpace)
            {
                buffer.Append(' ');
                addSpace = false;
            }

            buffer.Append(character);
        }

        return buffer.Length == 0 ? null : buffer.ToString();
    }

    public static NameComparisonState CompareName(
        string? left,
        string? right,
        NameComparisonContract contract)
    {
        var normalizedLeft = NormalizeText(left);
        var normalizedRight = NormalizeText(right);

        return contract switch
        {
            NameComparisonContract.WholeNameJaroWinklerV1 =>
                ClassifyWholeName(normalizedLeft, normalizedRight),
            NameComparisonContract.PtBrContentTokenGuardV2 =>
                ClassifyWithContentTokenGuard(normalizedLeft, normalizedRight),
            _ => throw new ArgumentOutOfRangeException(
                nameof(contract),
                contract,
                "Contrato nominal desconhecido na conferência independente.")
        };
    }

    public static string ClassifyBirth(DateOnly left, DateOnly right)
    {
        if (left == right)
            return "EXACT";

        if (left.Year == right.Year
            && left.Day == right.Month
            && left.Month == right.Day
            && (left.Day != left.Month || right.Day != right.Month))
            return "DAY_MONTH_SWAP";

        if (left.Day == right.Day
            && left.Month == right.Month
            && Math.Abs(left.Year - right.Year) == 100)
            return "CENTURY_SHIFT";

        var digitDistance = BirthDigitDistance(left, right);
        if (digitDistance == 1)
            return "ONE_DIGIT_ERROR";

        var matchingComponents =
            (left.Day == right.Day ? 1 : 0)
            + (left.Month == right.Month ? 1 : 0)
            + (left.Year == right.Year ? 1 : 0);

        if (matchingComponents >= 2)
            return "PARTIAL_COMPONENT_AGREEMENT";

        if (digitDistance == 2)
            return "TWO_DIGIT_ERROR";

        if (matchingComponents == 1)
            return "PARTIAL_COMPONENT_AGREEMENT";

        return "OTHER_DISAGREEMENT";
    }

    private static NameComparisonState ClassifyWholeName(string? left, string? right)
    {
        if (left is null || right is null)
            return NameComparisonState.LOW;

        if (string.Equals(left, right, StringComparison.Ordinal))
            return NameComparisonState.EXACT;

        return ClassifySimilarity(JaroWinkler(left, right));
    }

    private static NameComparisonState ClassifyWithContentTokenGuard(
        string? left,
        string? right)
    {
        if (left is null || right is null)
            return NameComparisonState.LOW;

        if (string.Equals(left, right, StringComparison.Ordinal))
            return NameComparisonState.EXACT;

        var similarity = JaroWinkler(left, right);
        var leftTokens = ContentTokens(left);
        var rightTokens = ContentTokens(right);

        if (leftTokens.Length == rightTokens.Length && leftTokens.Length > 1)
        {
            var weakest = 1d;
            for (var index = 0; index < leftTokens.Length; index++)
                weakest = Math.Min(
                    weakest,
                    JaroWinkler(leftTokens[index], rightTokens[index]));

            similarity = Math.Min(similarity, weakest);
        }

        return ClassifySimilarity(similarity);
    }

    private static string[] ContentTokens(string normalized) =>
        normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(static token => token is not ("DA" or "DAS" or "DE" or "DO" or "DOS"))
            .ToArray();

    private static NameComparisonState ClassifySimilarity(double similarity)
    {
        if (similarity >= 0.92d)
            return NameComparisonState.HIGH;
        if (similarity >= 0.80d)
            return NameComparisonState.MEDIUM;
        return NameComparisonState.LOW;
    }

    private static double JaroWinkler(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
            return 1d;
        if (left.Length == 0 || right.Length == 0)
            return 0d;

        var radius = Math.Max(left.Length, right.Length) / 2 - 1;
        if (radius < 0)
            radius = 0;

        var leftMatched = new bool[left.Length];
        var rightMatched = new bool[right.Length];
        var matches = 0;

        for (var leftIndex = 0; leftIndex < left.Length; leftIndex++)
        {
            var first = Math.Max(0, leftIndex - radius);
            var lastExclusive = Math.Min(
                leftIndex + radius + 1,
                right.Length);

            for (var rightIndex = first; rightIndex < lastExclusive; rightIndex++)
            {
                if (rightMatched[rightIndex]
                    || left[leftIndex] != right[rightIndex])
                    continue;

                leftMatched[leftIndex] = true;
                rightMatched[rightIndex] = true;
                matches++;
                break;
            }
        }

        if (matches == 0)
            return 0d;

        var transpositions = 0;
        var cursor = 0;
        for (var leftIndex = 0; leftIndex < left.Length; leftIndex++)
        {
            if (!leftMatched[leftIndex])
                continue;

            while (!rightMatched[cursor])
                cursor++;

            if (left[leftIndex] != right[cursor])
                transpositions++;

            cursor++;
        }

        var matched = (double)matches;
        var jaro =
            (matched / left.Length
             + matched / right.Length
             + (matched - transpositions / 2d) / matched)
            / 3d;

        var prefix = 0;
        var prefixLimit = Math.Min(4, Math.Min(left.Length, right.Length));
        while (prefix < prefixLimit && left[prefix] == right[prefix])
            prefix++;

        return jaro + prefix * 0.1d * (1d - jaro);
    }

    private static int BirthDigitDistance(DateOnly left, DateOnly right)
    {
        var leftText = left.ToString("ddMMyyyy", CultureInfo.InvariantCulture);
        var rightText = right.ToString("ddMMyyyy", CultureInfo.InvariantCulture);
        var distance = 0;

        for (var index = 0; index < leftText.Length; index++)
        {
            if (leftText[index] != rightText[index])
                distance++;
        }

        return distance;
    }
}
