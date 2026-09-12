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
/// Referência estatística imutável de nomes/sobrenomes vinculada ao modelo.
/// O hash hexadecimal é uma representação de auditoria do mesmo conteudo_sha256 persistido na versão.
/// Null identifica explicitamente modelos legados anteriores à adoção da referência interna.
/// </summary>
public sealed record ProbabilisticLinkageNameFrequencyReferenceRef(
    long VersionId,
    string VersionCode,
    string ContentSha256Hex);

/// <summary>
/// Referência imutável do modelo probabilístico capturado no início de uma execução.
/// Um mesmo linkage_run_id nunca mistura versões de modelo, referência estatística nem contratos de blocking.
/// </summary>
public sealed record ProbabilisticLinkageModelRef(
    Guid ModelId,
    int Version,
    string AlgorithmVersion,
    decimal Threshold,
    decimal ConflictMargin)
{
    public ProbabilisticLinkageBlockingContractRef? BlockingContract { get; init; }
    public ProbabilisticLinkageNameFrequencyReferenceRef? NameFrequencyReference { get; init; }
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
