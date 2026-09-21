namespace Jornada.Contracts;

/// <summary>
/// Identidade auditável do contrato de blocking congelado junto com o modelo.
/// Null no model ref significa explicitamente o caminho legado sem ruleset persistido.
/// </summary>
public sealed record ProbabilisticLinkageBlockingContractRef(
    string RuleSetVersion,
    string RuleSetFingerprintSha256,
    string? ProjectionSchemaVersion,
    string? ProjectionFingerprintSha256);

/// <summary>
/// Referência imutável do modelo probabilístico capturado no início de uma execução.
/// Um mesmo linkage_run_id nunca mistura versões de modelo nem contratos de blocking.
/// </summary>
public sealed record ProbabilisticLinkageModelRef(
    Guid ModelId,
    int Version,
    string AlgorithmVersion,
    decimal Threshold,
    decimal ConflictMargin)
{
    public ProbabilisticLinkageBlockingContractRef? BlockingContract { get; init; }
}

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
/// MelhorScore e SegundoScore são posteriores em [0,1]. Margem é não negativa e seu
/// espaço é versionado pela proveniência do modelo: contratos legados usam diferença
/// de posterior; DECISION_EVIDENCE_V6 usa diferença de log-odds antes da sigmoide.
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
    DateTimeOffset? PublicadoEm)
{
    /// <summary>
    /// Em INCREMENTAL, observações que ainda não tinham resultado probabilístico terminal.
    /// Zero nos demais modos.
    /// </summary>
    public long FreshPending { get; init; }

    /// <summary>
    /// Em INCREMENTAL, observações já publicadas como NAO_RESOLVIDO/CONFLITO
    /// que retornaram ao universo porque o lado candidato pode ter mudado.
    /// Zero nos demais modos.
    /// </summary>
    public long Reavaliados { get; init; }
}

public interface IProbabilisticLinkageBatchRunner
{
    Task<ProbabilisticLinkageRunSummary> RunAsync(
        ProbabilisticLinkageRunRequest request,
        CancellationToken ct);
}
