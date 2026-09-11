using System.Text;

namespace Jornada.Contracts;

/// <summary>
/// Canonicalizações determinísticas compartilhadas por Processor e Linkage.
/// As versões fazem parte do contrato persistido e devem permanecer semanticamente
/// idênticas às funções SQL correspondentes.
/// </summary>
public static class ContactCanonicalization
{
    public const string BrazilianPhoneVersion = "TELEFONE_BR_CANONICO_V2";
    public const string EmailVersion = "EMAIL_CANONICO_V2";

    private static readonly char[] EnvelopeTrimChars = [' ', '\t', '\r', '\n', '\u00A0'];

    public static string NormalizeBrazilianPhoneV2(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var trimmed = value.Trim(EnvelopeTrimChars);
        var explicitInternational = trimmed.StartsWith('+') || trimmed.StartsWith("00", StringComparison.Ordinal);
        var digits = DigitsOnly(trimmed);

        if (explicitInternational)
        {
            if (digits.StartsWith("00", StringComparison.Ordinal))
                digits = digits[2..];
            if (digits.Length is < 8 or > 15)
                throw new InvalidDataException("Telefone internacional deve conter de 8 a 15 dígitos após o prefixo internacional.");
            return digits;
        }

        if (digits.Length is 10 or 11)
            return "55" + digits;
        if (digits.Length is 12 or 13 && digits.StartsWith("55", StringComparison.Ordinal))
            return digits;

        throw new InvalidDataException("Telefone sem prefixo internacional deve informar DDD+número (10/11 dígitos) ou E.164 brasileiro iniciado por 55.");
    }

    public static string NormalizeEmailV2(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var trimmed = value.Trim(EnvelopeTrimChars);
        var at = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != trimmed.LastIndexOf('@') || at == trimmed.Length - 1)
            throw new InvalidDataException("E-mail inválido para EMAIL_CANONICO_V2.");

        var normalized = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
            normalized.Append(c is >= 'A' and <= 'Z' ? (char)(c + 32) : c);
        return normalized.ToString();
    }

    private static string DigitsOnly(string value)
    {
        var digits = new StringBuilder(value.Length);
        foreach (var c in value)
            if (char.IsAsciiDigit(c))
                digits.Append(c);
        return digits.ToString();
    }
}
