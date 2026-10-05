namespace Jornada.Api;

internal static class QueryFilterValidation
{
    public static IResult? Validate(
        string? natureza,
        string? codigo,
        DateOnly? desde,
        DateOnly? ate)
    {
        if (!string.IsNullOrWhiteSpace(natureza)
            && !string.Equals(natureza, "BENEFICIO", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(natureza, "SERVICO", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { erro = "natureza deve ser BENEFICIO ou SERVICO." });

        if (!string.IsNullOrWhiteSpace(codigo) && !IsValidCode(codigo))
            return Results.BadRequest(new { erro = "codigo deve ter exatamente 4 caracteres alfanuméricos." });

        if (desde.HasValue && ate.HasValue && desde.Value > ate.Value)
            return Results.BadRequest(new { erro = "desde não pode ser posterior a ate." });

        return null;
    }

    internal static bool IsValidCode(string codigo) =>
        codigo.Length == 4 && codigo.All(static character =>
            character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9');
}
