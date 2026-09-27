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
/// Token opaco de curta duração, sem UUID, CPF ou evidência estatística.
/// A validade do token deve ser conferida pelo serviço antes de qualquer confirmação.
/// </summary>
public sealed record SemiblindIdentityCandidate(
    string TokenConfirmacao,
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
