namespace Jornada.Contracts;

/// <summary>
/// Estado público da identidade progressiva. Não substitui o estado de uma ocorrência factual,
/// a vigência de um vínculo, a situação operacional de identidade.pessoa nem a âncora CPF.
/// </summary>
public enum ProgressiveIdentityStatus
{
    PROVISORIA,
    REFERENCIA,
    INDEFINIDA
}

/// <summary>Resultado de uma execução de resolução, não uma classificação adicional de Pessoa.</summary>
public enum ProgressiveResolutionOutcome
{
    NOVA_IDENTIDADE,
    ASSOCIACAO_EXISTENTE,
    INDEFINIDA
}

/// <summary>
/// Referência estável de uma identidade de origem. O UUID inicial nunca é reciclado.
/// CanonicalUuid identifica a referência canônica estabelecida pela política vigente;
/// não implica certeza absoluta de identidade civil nem validade de toda atribuição factual.
/// </summary>
public sealed record ProgressiveIdentitySnapshot(
    Guid InitialUuid,
    Guid? CanonicalUuid,
    ProgressiveIdentityStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastResolutionAt,
    ProgressiveIdentityDecision? LastDecision,
    Guid? LastExternalAssociationUuid);

/// <summary>
/// Recibo de uma execução completa. A validade da política, da amostra e das evidências
/// é atestada pelo executor externo; este contrato não homologa modelos estatísticos.
/// Referências são opacas e não devem conter CPF ou atributos pessoais em claro.
/// </summary>
public sealed record ProgressiveIdentityDecision(
    Guid DecisionId,
    Guid InitialUuid,
    long ExpectedVersion,
    ProgressiveResolutionOutcome Outcome,
    Guid? TargetUuid,
    bool Complete,
    string EvidenceReference,
    string PolicyVersion,
    DateTimeOffset DecidedAt,
    string? ModelVersion = null,
    string? UniverseReference = null);

/// <summary>
/// Núcleo puro da V1. Não persiste, não executa score, não cria aliases,
/// não modifica fatos e não realiza fusões ou separações de agregados.
/// </summary>
public static class ProgressiveIdentityLifecycle
{
    public const string Version = "PROGRESSIVE_IDENTITY_V1";

    public static ProgressiveIdentitySnapshot Create(Guid initialUuid, DateTimeOffset createdAt)
    {
        if (initialUuid == Guid.Empty)
            throw new ArgumentException("UUID inicial vazio.", nameof(initialUuid));
        RequireUtc(createdAt, nameof(createdAt));
        return new ProgressiveIdentitySnapshot(initialUuid, null,
            ProgressiveIdentityStatus.PROVISORIA, 0, createdAt, null, null, null);
    }

    public static ProgressiveIdentitySnapshot Conclude(
        ProgressiveIdentitySnapshot current, ProgressiveIdentityDecision decision)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(decision);
        ValidateSnapshot(current);
        if (decision.DecisionId == Guid.Empty || decision.InitialUuid != current.InitialUuid ||
            !Enum.IsDefined(decision.Outcome) || !decision.Complete ||
            string.IsNullOrWhiteSpace(decision.EvidenceReference) ||
            string.IsNullOrWhiteSpace(decision.PolicyVersion) ||
            decision.DecidedAt < current.CreatedAt ||
            (current.LastResolutionAt is { } last && decision.DecidedAt < last))
            throw new ArgumentException("Recibo de resolução incompleto ou incompatível.", nameof(decision));
        RequireUtc(decision.DecidedAt, nameof(decision));

        // Reexecução da mesma decisão é idempotente; reutilizar o ID com outro conteúdo não é.
        if (current.LastDecision is { } previous && previous.DecisionId == decision.DecisionId)
        {
            if (previous != decision)
                throw new InvalidOperationException("Identificador de decisão reutilizado com conteúdo diferente.");
            return current;
        }
        if (decision.ExpectedVersion != current.Version || current.Version == long.MaxValue)
            throw new InvalidOperationException("Versão de identidade obsoleta ou esgotada.");

        Guid? target;
        ProgressiveIdentityStatus status;
        switch (decision.Outcome)
        {
            case ProgressiveResolutionOutcome.NOVA_IDENTIDADE:
                if (decision.TargetUuid is not null || string.IsNullOrWhiteSpace(decision.UniverseReference))
                    throw new ArgumentException("Nova identidade exige universo completo e não aceita destino.", nameof(decision));
                // Uma associação anterior não pode ser desfeita implicitamente por ausência de candidatos.
                // A separação exige recomposição atômica do agregado.
                if (current.LastExternalAssociationUuid is not null)
                    throw new InvalidOperationException("Associação anterior exige separação explícita antes de retornar ao UUID inicial.");
                target = current.InitialUuid;
                status = ProgressiveIdentityStatus.REFERENCIA;
                break;
            case ProgressiveResolutionOutcome.ASSOCIACAO_EXISTENTE:
                if (decision.TargetUuid is not { } candidate || candidate == Guid.Empty)
                    throw new ArgumentException("Associação exige UUID de destino válido.", nameof(decision));
                if (current.LastExternalAssociationUuid is { } previousTarget && previousTarget != candidate)
                    throw new InvalidOperationException("Mudança de destino exige recomposição explícita do agregado.");
                target = candidate;
                status = ProgressiveIdentityStatus.REFERENCIA;
                break;
            case ProgressiveResolutionOutcome.INDEFINIDA:
                if (decision.TargetUuid is not null)
                    throw new ArgumentException("Resultado indefinido não pode escolher um destino.", nameof(decision));
                target = null;
                status = ProgressiveIdentityStatus.INDEFINIDA;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(decision));
        }

        return current with
        {
            CanonicalUuid = target,
            Status = status,
            Version = checked(current.Version + 1),
            LastResolutionAt = decision.DecidedAt,
            LastDecision = decision,
            LastExternalAssociationUuid = target is { } assigned && assigned != current.InitialUuid
                ? assigned : current.LastExternalAssociationUuid
        };
    }

    private static void ValidateSnapshot(ProgressiveIdentitySnapshot current)
    {
        if (current.InitialUuid == Guid.Empty)
            InvalidSnapshot("PI_SNAPSHOT_INITIAL_UUID_EMPTY");
        if (current.CanonicalUuid == Guid.Empty)
            InvalidSnapshot("PI_SNAPSHOT_CANONICAL_UUID_EMPTY");
        if (current.LastExternalAssociationUuid == Guid.Empty)
            InvalidSnapshot("PI_SNAPSHOT_EXTERNAL_UUID_EMPTY");
        if (!Enum.IsDefined(current.Status))
            InvalidSnapshot("PI_SNAPSHOT_STATUS_INVALID");
        if (current.Version < 0)
            InvalidSnapshot("PI_SNAPSHOT_VERSION_NEGATIVE");
        if (current.Version == 0 && current.Status != ProgressiveIdentityStatus.PROVISORIA)
            InvalidSnapshot("PI_SNAPSHOT_VERSION_ZERO_NOT_PROVISIONAL");

        if (current.Status == ProgressiveIdentityStatus.PROVISORIA)
        {
            if (current.Version != 0)
                InvalidSnapshot("PI_SNAPSHOT_PROVISIONAL_VERSION_NONZERO");
            if (current.CanonicalUuid is not null)
                InvalidSnapshot("PI_SNAPSHOT_PROVISIONAL_WITH_CANONICAL");
            if (current.LastResolutionAt is not null)
                InvalidSnapshot("PI_SNAPSHOT_PROVISIONAL_WITH_RESOLUTION");
            if (current.LastDecision is not null)
                InvalidSnapshot("PI_SNAPSHOT_PROVISIONAL_WITH_RECEIPT");
            if (current.LastExternalAssociationUuid is not null)
                InvalidSnapshot("PI_SNAPSHOT_PROVISIONAL_WITH_EXTERNAL_ASSOCIATION");
        }

        if (current.Status == ProgressiveIdentityStatus.REFERENCIA && current.CanonicalUuid is null)
            InvalidSnapshot("PI_SNAPSHOT_REFERENCE_WITHOUT_CANONICAL");
        if (current.Status == ProgressiveIdentityStatus.INDEFINIDA && current.CanonicalUuid is not null)
            InvalidSnapshot("PI_SNAPSHOT_UNDEFINED_WITH_CANONICAL");
        if (current.Version > 0 && current.LastResolutionAt is null)
            InvalidSnapshot("PI_SNAPSHOT_VERSION_WITHOUT_RESOLUTION");
        if (current.Version > 0 && current.LastDecision is null)
            InvalidSnapshot("PI_SNAPSHOT_VERSION_WITHOUT_RECEIPT");

        if (current.LastExternalAssociationUuid is { } prior)
        {
            if (prior == current.InitialUuid)
                InvalidSnapshot("PI_SNAPSHOT_EXTERNAL_EQUALS_INITIAL");
            if (current.CanonicalUuid is { } canonical && canonical != prior)
                InvalidSnapshot("PI_SNAPSHOT_EXTERNAL_CANONICAL_MISMATCH");
        }

        if (current.CreatedAt == default || current.CreatedAt.Offset != TimeSpan.Zero)
            InvalidSnapshot("PI_SNAPSHOT_CREATED_AT_NOT_UTC");

        if (current.LastDecision is { } receipt)
        {
            if (receipt.InitialUuid != current.InitialUuid)
                InvalidSnapshot("PI_SNAPSHOT_RECEIPT_INITIAL_UUID_MISMATCH");
            if (receipt.ExpectedVersion != current.Version - 1)
                InvalidSnapshot("PI_SNAPSHOT_RECEIPT_VERSION_MISMATCH");
            if (receipt.DecidedAt != current.LastResolutionAt)
                InvalidSnapshot("PI_SNAPSHOT_RECEIPT_TIMESTAMP_MISMATCH");
            if (!receipt.Complete)
                InvalidSnapshot("PI_SNAPSHOT_RECEIPT_INCOMPLETE");
            if ((receipt.Outcome == ProgressiveResolutionOutcome.INDEFINIDA) !=
                (current.Status == ProgressiveIdentityStatus.INDEFINIDA))
                InvalidSnapshot("PI_SNAPSHOT_RECEIPT_OUTCOME_STATUS_MISMATCH");
            if (current.Status == ProgressiveIdentityStatus.REFERENCIA &&
                receipt.Outcome == ProgressiveResolutionOutcome.ASSOCIACAO_EXISTENTE &&
                receipt.TargetUuid != current.CanonicalUuid)
                InvalidSnapshot("PI_SNAPSHOT_RECEIPT_TARGET_CANONICAL_MISMATCH");
            if (current.Status == ProgressiveIdentityStatus.REFERENCIA &&
                receipt.Outcome == ProgressiveResolutionOutcome.NOVA_IDENTIDADE &&
                current.CanonicalUuid != current.InitialUuid)
                InvalidSnapshot("PI_SNAPSHOT_RECEIPT_NEW_IDENTITY_CANONICAL_MISMATCH");
        }

        if (current.LastResolutionAt is { } last)
        {
            if (last == default || last.Offset != TimeSpan.Zero)
                InvalidSnapshot("PI_SNAPSHOT_RESOLUTION_AT_NOT_UTC");
            if (last < current.CreatedAt)
                InvalidSnapshot("PI_SNAPSHOT_RESOLUTION_BEFORE_CREATION");
        }
    }

    private static void InvalidSnapshot(string code) =>
        throw new InvalidOperationException(code);

    private static void RequireUtc(DateTimeOffset value, string parameter)
    {
        if (value == default || value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Instante UTC obrigatório.", parameter);
    }
}
