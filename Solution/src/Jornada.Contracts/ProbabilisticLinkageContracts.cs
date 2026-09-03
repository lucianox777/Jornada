namespace Jornada.Contracts;

/// <summary>
/// Referência imutável do modelo probabilístico capturado no início de uma execução.
/// Um mesmo linkage_run_id nunca mistura versões de modelo.
/// </summary>
public sealed record ProbabilisticLinkageModelRef(
    Guid ModelId,
    int Version,
    string AlgorithmVersion,
    decimal Threshold,
    decimal ConflictMargin);

public enum LinkageRunType { ON_DEMAND, INCREMENTAL, REPLAY, FULL, MODEL_VALIDATION }
public enum LinkageRunStatus { PREPARANDO, EXECUTANDO, PUBLICADO, CONCLUIDO_SEM_PUBLICACAO, FALHOU, CANCELADO }

/// <summary>
/// Parâmetros congelados de uma execução. A seleção do modelo e o escopo são registrados
/// antes do processamento da primeira observação.
/// </summary>
public sealed record ProbabilisticLinkageRunRequest(
    LinkageRunType Mode,
    int? ModelVersion,
    long? PessoaObservacaoId,
    string? GestorCodigo,
    DateTimeOffset? Since,
    int BatchSize,
    int MaxParallelism,
    long? MaxRecords,
    string? RequestedBy,
    string? Reason,
    Guid CorrelationId,
    bool Publish);

/// <summary>
/// Resultado interno detalhado de uma observação. Persistem-se apenas os dois
/// melhores candidatos, nunca a lista completa de candidatos do blocking.
/// </summary>
public sealed record ProbabilisticLinkageDecision(
    ResolutionStatus Status,
    Guid? PessoaUuidResolvido,
    Guid? MelhorCandidatoUuid,
    decimal MelhorScore,
    Guid? SegundoCandidatoUuid,
    decimal? SegundoScore,
    decimal? Margem,
    Guid ModeloId,
    string? Motivo = null);

public sealed record ProbabilisticLinkageRunSummary(
    Guid RunId,
    Guid ModeloId,
    int ModeloVersao,
    LinkageRunStatus Status,
    long Elegiveis,
    long Avaliados,
    long Resolvidos,
    long NaoResolvidos,
    long Conflitos,
    long SemCandidatoNoBloco,
    DateTimeOffset IniciadoEm,
    DateTimeOffset FinalizadoEm,
    DateTimeOffset? PublicadoEm);

public interface IProbabilisticLinkageBatchRunner
{
    Task<ProbabilisticLinkageRunSummary> RunAsync(
        ProbabilisticLinkageRunRequest request,
        CancellationToken ct);
}
