using System.Text.Json.Serialization;

namespace Jornada.Contracts;

/// <summary>
/// Consulta semicega: o atendente fornece apenas evidências de identidade.
/// A ausência de nascimento não deve ser interpretada como data aproximada.
/// </summary>
public sealed record SemiblindIdentitySearchRequest(
    [property: JsonPropertyName("nome_completo")] string? Nome,
    [property: JsonPropertyName("data_nascimento")] DateOnly? DataNascimento,
    [property: JsonPropertyName("nome_mae")] string? NomeMae);

/// <summary>
/// Identificador opaco apenas para seleção na interface; não autoriza confirmação.
/// </summary>
public sealed record SemiblindIdentityCandidate(
    string OpcaoId,
    [property: JsonPropertyName("nome_completo")] string Nome,
    [property: JsonPropertyName("data_nascimento")] DateOnly? DataNascimento,
    [property: JsonPropertyName("nome_mae")] string? NomeMae);

/// <summary>
/// Contrato externo deliberadamente sem score, LLR, posterior ou posição.
/// NenhumDestes permanece true inclusive quando Candidatos está vazio.
/// </summary>
public sealed record SemiblindIdentitySearchResponse(
    Guid ConsultaId,
    IReadOnlyList<SemiblindIdentityCandidate> Candidatos,
    [property: JsonPropertyName("nenhumDestes")] bool NenhumDestes = true,
    [property: JsonPropertyName("busca_completa")] bool BuscaCompleta = true,
    [property: JsonPropertyName("motivo_incompletude")] string? MotivoIncompletude = null);

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

public static class SemiblindRetrievalReasons
{
    public const string NoEligiblePass = "SEM_PASSE_ELEGIVEL";
    public const string FanoutLimitExceeded = "LIMITE_FANOUT_EXCEDIDO";
    public const string Timeout = "TIMEOUT_RECUPERACAO";
}

public sealed record SemiblindCandidateRetrievalResult(
    bool Completed,
    IReadOnlyList<SemiblindInternalCandidate> Candidates,
    string? IncompleteReason = null)
{
    public static SemiblindCandidateRetrievalResult Complete(IReadOnlyList<SemiblindInternalCandidate> candidates) =>
        new(true, candidates);

    public static SemiblindCandidateRetrievalResult Incomplete(string reason) =>
        new(false, Array.Empty<SemiblindInternalCandidate>(), reason);
}

public interface ISemiblindCandidateRetriever
{
    Task<SemiblindCandidateRetrievalResult> RetrieveAsync(
        SemiblindIdentitySearchRequest request, CancellationToken cancellationToken);
}
