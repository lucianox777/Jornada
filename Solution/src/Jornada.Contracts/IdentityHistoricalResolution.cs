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
        // O planejador é a autoridade única das invariantes e da classificação histórica.
        // Esta projeção acrescenta proveniência e membros indefinidos, sem duplicar a regra.
        var resolution = IdentityCompositionPlanner.ResolveHistorical(history, current);
        var members = current.ToDictionary(m => m.InitialUuid);
        var unresolved = history.MemberInitialUuids
            .Where(id => members[id].CanonicalUuid is null)
            .Order().ToImmutableArray();
        return new IdentityHistoricalResolutionResult(history.ReferenceUuid, history.CompositionId,
            resolution.State, resolution.CanonicalUuid, resolution.Candidates, unresolved);
    }
}
