namespace Jornada.Contracts;

/// <summary>
/// Projeção mínima que a Jornada consegue derivar de <c>nome_completo</c> sem
/// inventar a separação original entre nome composto e sobrenomes coletada pelo IBGE.
/// </summary>
public sealed record IbgePublishedNameProjection(
    string FirstName,
    string FirstNameNormalized);

/// <summary>
/// Semântica da divulgação "Censo Demográfico 2022 - Nomes no Brasil".
///
/// A nota técnica do IBGE informa que o campo de nome podia receber primeiro nome
/// ou nome composto, mas, para divulgação, foi considerado somente o primeiro nome
/// informado. Os sobrenomes eram coletados em campo separado e publicados sem
/// significado posicional.
///
/// Como a Jornada possui <c>nome_completo</c>, mas não preserva a fronteira do campo
/// original nome/sobrenome do Censo, somente o primeiro nome é derivável de forma
/// inequívoca. Este componente deliberadamente não infere sobrenomes.
/// </summary>
public static class IbgeNamePublicationSemantics
{
    public const string MethodVersion = "IBGE_CENSO_2022_NOMES_PUBLICACAO_V1";

    public static IbgePublishedNameProjection? ProjectFirstName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return null;

        var tokens = fullName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return null;

        var firstName = tokens[0].Trim();
        var normalized = IdentityComparison.NormalizeText(firstName);
        if (normalized is null)
            return null;

        return new IbgePublishedNameProjection(firstName, normalized);
    }
}
