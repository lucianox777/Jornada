namespace Jornada.Contracts;

/// <summary>Normalização e validação estrutural de CPF compartilhada pela API e pelo Processor.</summary>
public static class CpfRules
{
    public static string? NormalizeAndValidate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length != 11 || digits.Distinct().Count() == 1)
            return null;

        var numbers = digits.Select(c => c - '0').ToArray();
        if (CalculateDigit(numbers, 9, 10) != numbers[9])
            return null;
        if (CalculateDigit(numbers, 10, 11) != numbers[10])
            return null;

        return digits;
    }

    private static int CalculateDigit(int[] numbers, int length, int startWeight)
    {
        var sum = 0;
        for (var i = 0; i < length; i++)
            sum += numbers[i] * (startWeight - i);

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
