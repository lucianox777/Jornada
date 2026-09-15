using System.Globalization;

namespace Jornada.Contracts;

/// <summary>
/// Estados semânticos mutuamente exclusivos para a única evidência de data de nascimento.
/// A classificação descreve a transformação observada entre duas datas; não atribui peso.
/// Os pesos m/u continuam sendo estimados pelo calibrador no universo do blocking.
/// </summary>
public static class BirthDateSemanticEvidence
{
    public const string Exact = "EXACT";
    public const string DayMonthSwap = "DAY_MONTH_SWAP";
    public const string CenturyShift = "CENTURY_SHIFT";
    public const string OneDigitError = "ONE_DIGIT_ERROR";
    public const string TwoDigitError = "TWO_DIGIT_ERROR";
    public const string PartialComponentAgreement = "PARTIAL_COMPONENT_AGREEMENT";
    public const string OtherDisagreement = "OTHER_DISAGREEMENT";

    public static readonly IReadOnlyList<string> States =
    [
        Exact,
        DayMonthSwap,
        CenturyShift,
        OneDigitError,
        TwoDigitError,
        PartialComponentAgreement,
        OtherDisagreement
    ];

    public static string Classify(DateOnly left, DateOnly right)
    {
        if (left == right)
            return Exact;

        if (IsDayMonthSwap(left, right))
            return DayMonthSwap;

        if (left.Day == right.Day &&
            left.Month == right.Month &&
            Math.Abs(left.Year - right.Year) == 100)
            return CenturyShift;

        var digitDistance = DigitDistance(left, right);
        if (digitDistance == 1)
            return OneDigitError;

        // Duas componentes inteiras concordantes constituem evidência estrutural mais
        // específica do que uma diferença genérica de dois dígitos. Uma única componente,
        // por outro lado, não deve esconder um erro cadastral de dois dígitos reconhecível.
        var matchingComponents = MatchingComponentCount(left, right);
        if (matchingComponents >= 2)
            return PartialComponentAgreement;

        if (digitDistance == 2)
            return TwoDigitError;

        if (matchingComponents == 1)
            return PartialComponentAgreement;

        return OtherDisagreement;
    }

    private static bool IsDayMonthSwap(DateOnly left, DateOnly right) =>
        left.Year == right.Year &&
        left.Day == right.Month &&
        left.Month == right.Day &&
        (left.Day != left.Month || right.Day != right.Month);

    private static int MatchingComponentCount(DateOnly left, DateOnly right) =>
        (left.Day == right.Day ? 1 : 0) +
        (left.Month == right.Month ? 1 : 0) +
        (left.Year == right.Year ? 1 : 0);

    private static int DigitDistance(DateOnly left, DateOnly right)
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
