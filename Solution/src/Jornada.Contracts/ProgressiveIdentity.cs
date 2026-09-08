namespace Jornada.Contracts;

/// <summary>
/// Estado público da resolução progressiva. Não substitui o estado de uma ocorrência
/// factual, a vigência de um vínculo, nem a situação operacional de identidade.pessoa.
/// </summary>
public enum ProgressiveIdentityStatus
{
    PROVISORIA,
    RESOLVIDA,
    INDEFINIDA
}

/// <summary>Resultado de uma execução, não uma classificação adicional de Pessoa.</summary>
public enum ProgressiveResolutionOutcome
{
    NOVA_IDENTIDADE,
    ASSOCIACAO_EXISTENTE,
    INDEFINIDA
}

/// <summary>
/// Referência estável de uma identidade de origem. O UUID inicial nunca é reciclado.
/// CanonicalUuid é uma atribuição proposta/confirmada pelo subsistema de resolução;
/// não deve ser confundido com o UUID inicial nem usado sem verificar o estado.
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
/// Núcleo puro da primeira fatia. Não persiste, não executa score, não cria aliases,
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
                // Uma associação anterior não pode ser desfeita implicitamente por um sorteio
                // sem candidatos. A separação exige recomposição atômica do agregado.
                if (current.LastExternalAssociationUuid is not null)
                    throw new InvalidOperationException("Associação anterior exige separação explícita antes de retornar ao UUID inicial.");
                target = current.InitialUuid;
                status = ProgressiveIdentityStatus.RESOLVIDA;
                break;
            case ProgressiveResolutionOutcome.ASSOCIACAO_EXISTENTE:
                if (decision.TargetUuid is not { } candidate || candidate == Guid.Empty)
                    throw new ArgumentException("Associação exige UUID de destino válido.", nameof(decision));
                if (current.LastExternalAssociationUuid is { } previousTarget && previousTarget != candidate)
                    throw new InvalidOperationException("Mudança de destino exige recomposição explícita do agregado.");
                target = candidate;
                status = ProgressiveIdentityStatus.RESOLVIDA;
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
        if (current.InitialUuid == Guid.Empty || current.CanonicalUuid == Guid.Empty ||
            current.LastExternalAssociationUuid == Guid.Empty ||
            !Enum.IsDefined(current.Status) || current.Version < 0 ||
            (current.Version == 0 && current.Status != ProgressiveIdentityStatus.PROVISORIA) ||
            (current.Status == ProgressiveIdentityStatus.PROVISORIA &&
                (current.Version != 0 || current.CanonicalUuid is not null ||
                 current.LastResolutionAt is not null || current.LastDecision is not null ||
                 current.LastExternalAssociationUuid is not null)) ||
            (current.Status == ProgressiveIdentityStatus.RESOLVIDA && current.CanonicalUuid is null) ||
            (current.Status == ProgressiveIdentityStatus.INDEFINIDA && current.CanonicalUuid is not null) ||
            (current.Version > 0 && (current.LastResolutionAt is null || current.LastDecision is null)) ||
            (current.LastExternalAssociationUuid is { } prior &&
                (prior == current.InitialUuid ||
                 (current.CanonicalUuid is { } canonical && canonical != prior))))
            throw new InvalidOperationException("Estado de identidade inconsistente.");
        RequireUtc(current.CreatedAt, nameof(current));
        if (current.LastDecision is { } receipt &&
            (receipt.InitialUuid != current.InitialUuid ||
             receipt.ExpectedVersion != current.Version - 1 ||
             receipt.DecidedAt != current.LastResolutionAt ||
             !receipt.Complete ||
             (receipt.Outcome == ProgressiveResolutionOutcome.INDEFINIDA) !=
                 (current.Status == ProgressiveIdentityStatus.INDEFINIDA) ||
             (current.Status == ProgressiveIdentityStatus.RESOLVIDA &&
                 receipt.Outcome == ProgressiveResolutionOutcome.ASSOCIACAO_EXISTENTE &&
                 receipt.TargetUuid != current.CanonicalUuid) ||
             (current.Status == ProgressiveIdentityStatus.RESOLVIDA &&
                 receipt.Outcome == ProgressiveResolutionOutcome.NOVA_IDENTIDADE &&
                 current.CanonicalUuid != current.InitialUuid)))
            throw new InvalidOperationException("Último recibo não corresponde à versão corrente.");
        if (current.LastResolutionAt is { } last)
        {
            RequireUtc(last, nameof(current));
            if (last < current.CreatedAt)
                throw new InvalidOperationException("Data de resolução anterior à criação.");
        }
    }

    private static void RequireUtc(DateTimeOffset value, string parameter)
    {
        if (value == default || value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Instante UTC obrigatório.", parameter);
    }
}
