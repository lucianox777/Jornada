namespace Jornada.Contracts;

/// <summary>
/// Observação mínima de identidade usada internamente pelo Processor.
/// CPF válido é a rota determinística normal. Os demais atributos são usados para
/// qualidade/corroboração, pela trava de consistência quando o CPF já estiver associado
/// a um UUID e, quando o CPF estiver ausente em hipótese admitida, pelo fallback probabilístico.
/// </summary>
public sealed record IdentityObservation(
    string? Cpf,
    string? CpfAusenteMotivo,
    string NomeCompleto,
    DateOnly DataNascimento,
    string NomeMae);

public sealed record InternalIdentityResolution(
    ResolutionStatus Status,
    Guid? PessoaUuid,
    ResolutionMethod MetodoResolucao,
    decimal? Score = null,
    Guid? ModeloId = null,
    string? Motivo = null);

public interface IIdentityMapRepository
{
    /// <summary>
    /// Localiza ou constitui o UUID técnico para um CPF estruturalmente válido.
    /// Quando o CPF já está associado a uma Pessoa, confronta o núcleo informado
    /// antes de criar novo vínculo de fonte. Divergência forte retorna CONFLITO,
    /// sem UUID, e nunca é rebaixada automaticamente ao linkage probabilístico.
    /// </summary>
    Task<InternalIdentityResolution> ResolveOrCreateByCpfAsync(
        string cpf,
        IdentityObservation observation,
        CancellationToken ct);
}

public interface IProbabilisticIdentityLinkage
{
    /// <summary>
    /// Captura a versão ATIVA que será congelada para toda a execução sob demanda.
    /// </summary>
    Task<ProbabilisticLinkageModelRef> GetActiveModelAsync(CancellationToken ct);

    /// <summary>
    /// Carrega uma versão explicitamente solicitada para replay, validação ou execução controlada.
    /// A versão precisa existir e estar VALIDADO/ATIVO/INATIVO; RASCUNHO não pode produzir score operacional.
    /// </summary>
    Task<ProbabilisticLinkageModelRef> GetModelByVersionAsync(int version, CancellationToken ct);

    /// <summary>
    /// Fallback exclusivo para observações sem CPF em hipótese admitida.
    /// O modelo é informado explicitamente para impedir mistura de versões dentro do mesmo run.
    /// </summary>
    Task<ProbabilisticLinkageDecision> ResolveWithoutCpfAsync(
        IdentityObservation observation,
        Guid modeloId,
        CancellationToken ct);
}
