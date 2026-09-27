namespace Jornada.Contracts;

/// <summary>
/// Consulta semicega: o atendente fornece apenas evidências de identidade.
/// A ausência de nascimento não deve ser interpretada como data aproximada.
/// </summary>
public sealed record SemiblindIdentitySearchRequest(
    string Nome,
    DateOnly? DataNascimento,
    string? NomeMae);

/// <summary>
/// Identificador opaco apenas para seleção na interface; não autoriza confirmação.
/// </summary>
public sealed record SemiblindIdentityCandidate(
    string OpcaoId,
    string Nome,
    DateOnly? DataNascimento,
    string? NomeMae);

/// <summary>
/// Contrato externo deliberadamente sem score, LLR, posterior ou posição.
/// NenhumDestesDisponivel permanece true inclusive quando Candidatos está vazio.
/// </summary>
public sealed record SemiblindIdentitySearchResponse(
    Guid ConsultaId,
    IReadOnlyList<SemiblindIdentityCandidate> Candidatos,
    bool NenhumDestesDisponivel = true);

public interface ISemiblindIdentitySearchService
{
    Task<SemiblindIdentitySearchResponse> SearchAsync(
        AccessContext context,
        SemiblindIdentitySearchRequest request,
        Guid correlationId,
        CancellationToken cancellationToken);
}

/// <summary>Projeção interna de candidatos; jamais serializar diretamente para o atendente.</summary>
public sealed record SemiblindInternalCandidate(Guid PessoaUuid, string? Nome, DateOnly? DataNascimento, string? NomeMae);

public interface ISemiblindCandidateRetriever
{
    Task<IReadOnlyList<SemiblindInternalCandidate>> RetrieveAsync(
        SemiblindIdentitySearchRequest request, CancellationToken cancellationToken);
}
