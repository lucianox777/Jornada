namespace Jornada.Contracts;

/// <summary>
/// Observação mínima de identidade usada internamente pelo Processor.
/// CPF válido é a rota determinística normal. Os demais atributos são usados para
/// qualidade/corroboração, para sinalizar inconsistências globais do identificador quando
/// o mesmo CPF aparece com núcleos fortemente incompatíveis e, quando o CPF estiver ausente
/// em hipótese admitida, pelo fallback probabilístico.
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
    /// Quando o CPF já está associado a uma Pessoa, o núcleo informado pode sinalizar
    /// inconsistência no próprio identificador, mas não altera nem suspende a resolução
    /// determinística CPF -> UUID. Nenhuma observação é automaticamente escolhida como errada.
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
