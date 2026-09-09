using System.Collections.Immutable;

namespace Jornada.Contracts;

/// <summary>Resultado histórico com proveniência e sem sucessor arbitrário.</summary>
public sealed record IdentityHistoricalResolutionResult(
    Guid ReferenceUuid, Guid CompositionId, HistoricalReferenceState State,
    Guid? CanonicalUuid, ImmutableArray<Guid> Candidates,
    ImmutableArray<Guid> UnresolvedInitialUuids);

/// <summary>Resolve snapshots aplicados. O adapter deve provar a completude da leitura corrente.</summary>
public static class IdentityHistoricalResolver
{
    public const string Version = "IDENTITY_HISTORICAL_RESOLUTION_V1";

    public static IdentityHistoricalResolutionResult Resolve(
        IdentityCompositionHistory history, ImmutableArray<IdentityCompositionMember> current)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (current.IsDefault) throw new ArgumentException("Leitura ausente.", nameof(current));
        var members = current.ToDictionary(m => m.InitialUuid);
        if (members.Count != current.Length || history.ReferenceUuid == Guid.Empty ||
            history.CompositionId == Guid.Empty || history.MemberInitialUuids.IsDefaultOrEmpty ||
            history.MemberInitialUuids.Any(id => id == Guid.Empty) ||
            history.MemberInitialUuids.Distinct().Count() != history.MemberInitialUuids.Length)
            throw new InvalidOperationException("Histórico inválido.");
        foreach (var member in current)
            if (member.InitialUuid == Guid.Empty || member.CanonicalUuid == Guid.Empty ||
                member.Version < 0 || !Enum.IsDefined(member.Status) ||
                (member.Status == ProgressiveIdentityStatus.REFERENCIA) != (member.CanonicalUuid is not null))
                throw new InvalidOperationException("Snapshot corrente inválido.");
        if (history.MemberInitialUuids.Any(id => !members.ContainsKey(id)))
            throw new InvalidOperationException("Leitura histórica incompleta.");
        var selected = history.MemberInitialUuids.Select(id => members[id]).ToArray();
        var candidates = selected.Where(m => m.CanonicalUuid.HasValue)
            .Select(m => m.CanonicalUuid!.Value).Distinct().Order().ToImmutableArray();
        var unresolved = selected.Where(m => !m.CanonicalUuid.HasValue)
            .Select(m => m.InitialUuid).Order().ToImmutableArray();
        var state = candidates.Length > 1 ? HistoricalReferenceState.AMBIGUA :
            unresolved.Length > 0 || candidates.Length == 0 ? HistoricalReferenceState.INDEFINIDA :
            HistoricalReferenceState.UNIVOCA;
        return new IdentityHistoricalResolutionResult(history.ReferenceUuid, history.CompositionId,
            state, state == HistoricalReferenceState.UNIVOCA ? candidates[0] : null,
            candidates, unresolved);
    }
}
