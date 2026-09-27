using System.Text.Json.Serialization;

namespace Jornada.Contracts;

/// <summary>Busca ad hoc somente por atributos demográficos; CPF e outros campos não são aceitos.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IdentityCandidateSearchRequest(
    string? NomeCompleto,
    DateOnly? DataNascimento,
    string? NomeMae);

/// <summary>Somente os atributos mínimos de confirmação; nunca contém CPF nem evidência probabilística.</summary>
public sealed record IdentityCandidateDto(
    Guid PessoaUuid,
    [property: JsonPropertyName("nome_completo")] string? NomeCompleto,
    [property: JsonPropertyName("data_nascimento")] DateOnly? DataNascimento,
    [property: JsonPropertyName("nome_mae")] string? NomeMae);

/// <summary>
/// NenhumDestes=true quando não houve candidato recuperável. A interface oferece
/// "Nenhum destes" também quando existem sugestões; este boolean não decide identidade.
/// </summary>
public sealed record IdentityCandidateSearchResponse(
    IReadOnlyList<IdentityCandidateDto> Candidatos,
    bool NenhumDestes);
