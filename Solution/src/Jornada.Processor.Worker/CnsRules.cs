namespace Jornada.Processor.Worker;

/// <summary>
/// Validação estrutural do Cartão Nacional de Saúde (CNS) para a futura
/// propriedade opcional da SMS. Não prova existência no CADSUS e nunca é
/// evidência determinística de identidade.
/// Algoritmos: ANS, "Algoritmos do Aplicativo de Carga" (SIB.XML), seção 4.
/// </summary>
internal static class CnsRules
{
    /// <summary>
    /// Usa o algoritmo baseado em PIS para prefixos 1/2 e o módulo 11
    /// ponderado para 7/8/9. Não normaliza nem altera o valor transmitido:
    /// o contrato exige exatamente 15 dígitos ASCII.
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (value is null || value.Length != 15 || value == "000000000000000")
            return false;
        foreach (var digit in value)
            if (digit is < '0' or > '9')
                return false;

        if (value[0] is '7' or '8' or '9')
        {
            var total = 0;
            for (var i = 0; i < 15; i++)
                total += (value[i] - '0') * (15 - i);
            return total % 11 == 0;
        }

        if (value[0] is not ('1' or '2'))
            return false;

        var weighted = 0;
        for (var i = 0; i < 11; i++)
            weighted += (value[i] - '0') * (15 - i);

        var verifier = 11 - weighted % 11;
        if (verifier == 11)
            verifier = 0;

        var suffix = "000";
        if (verifier == 10)
        {
            suffix = "001";
            verifier = 11 - (weighted + 2) % 11;
        }

        var expected = value[..11] + suffix
            + verifier.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return string.Equals(value, expected, StringComparison.Ordinal);
    }

    /// <summary>
    /// A ausência (null) é válida; texto vazio, máscara, DV inválido e
    /// prefixos desconhecidos são rejeitados sem revelar o CNS em logs.
    /// </summary>
    public static void ValidateOptionalSmsCns(string? value)
    {
        if (value is null)
            return;
        if (!IsValid(value))
            throw new InvalidDataException("SMS: CNS deve conter 15 dígitos e dígito verificador válido.");
    }
}
