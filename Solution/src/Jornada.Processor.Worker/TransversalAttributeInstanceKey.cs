using System.Text;
using Jornada.Contracts;

namespace Jornada.Processor.Worker;

internal static class TransversalAttributeInstanceKey
{
    internal const string SingleKey = "#";

    public static string Compute(string cardinality, string keyRule, string value)
    {
        if (string.Equals(cardinality, "SINGLE", StringComparison.OrdinalIgnoreCase))
            return SingleKey;
        if (!string.Equals(cardinality, "MULTI", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Cardinalidade de atributo transversal não suportada: {cardinality}.");

        var key = keyRule switch
        {
            ContactCanonicalization.BrazilianPhoneVersion => ContactCanonicalization.NormalizeBrazilianPhoneV2(value),
            // Compatibilidade técnica legada: o catálogo vigente não seleciona V1 automaticamente no replay.
            "TELEFONE_DIGITOS_V1" => NormalizeDigitsOnlyLegacy(value),
            // Regra legada preservada para replay/migração histórica; o catálogo vigente usa V2.
            "EMAIL_CASEFOLD_V1" => NormalizeEmailLegacyV1(value),
            ContactCanonicalization.EmailVersion => ContactCanonicalization.NormalizeEmailV2(value),
            _ => throw new InvalidDataException($"Regra de chave de instância não suportada para atributo MULTI: {keyRule}.")
        };
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidDataException("A chave normalizada de atributo MULTI não pode ser vazia.");
        if (key.Length > 512)
            throw new InvalidDataException("A chave normalizada de atributo MULTI excede 512 caracteres.");
        return key;
    }

    private static string NormalizeDigitsOnlyLegacy(string value)
    {
        var digits = DigitsOnly(value);
        if (digits.Length is < 8 or > 15)
            throw new InvalidDataException("TELEFONE_CONTATO deve produzir de 8 a 15 dígitos na normalização legada da chave de instância.");
        return digits;
    }

    private static string DigitsOnly(string value)
    {
        var digits = new StringBuilder(value.Length);
        foreach (var c in value)
            if (char.IsAsciiDigit(c)) digits.Append(c);
        return digits.ToString();
    }

    private static readonly char[] EmailEnvelopeTrimChars = [' ', '\t', '\r', '\n', '\u00A0'];

    private static string NormalizeEmailLegacyV1(string value)
    {
        // Semântica histórica da linha v3.66. Não é usada pelo catálogo vigente porque
        // NFC + ToLowerInvariant não possui equivalente determinístico simples em T-SQL.
        var trimmed = value.Trim(EmailEnvelopeTrimChars).Normalize(NormalizationForm.FormC);
        var at = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != trimmed.LastIndexOf('@') || at == trimmed.Length - 1)
            throw new InvalidDataException("EMAIL_CONTATO inválido para chave de instância.");
        return trimmed.ToLowerInvariant();
    }
}
