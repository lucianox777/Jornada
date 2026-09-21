namespace Jornada.Contracts;

/// <summary>
/// Normalização e validação estrutural local de NIS/PIS/PASEP/NIT.
/// A validação estrutural não comprova titularidade nem situação cadastral no CNIS.
/// </summary>
public static class NisRules
{
    public const string StructurallyInvalidReason = "NIS_ESTRUTURALMENTE_INVALIDO";

    public static string? NormalizeAndValidate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length != 11 || digits.Distinct().Count() == 1)
            return null;

        int[] weights = [3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        var sum = 0;
        for (var i = 0; i < 10; i++)
            sum += (digits[i] - '0') * weights[i];

        var check = 11 - (sum % 11);
        if (check is 10 or 11)
            check = 0;

        return check == digits[10] - '0' ? digits : null;
    }
}
