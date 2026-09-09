using System.Collections.Immutable;

namespace Jornada.Contracts;

/// <summary>Fechamento monotônico de referências e membros históricos. Não autoriza aplicação.</summary>
public static class IdentityCompositionHistoryClosure
{
    public static ImmutableArray<Guid> MissingMembers(
        IEnumerable<IdentityCompositionHistory> history, IEnumerable<Guid> loadedInitialUuids)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(loadedInitialUuids);
        var loaded = loadedInitialUuids.ToHashSet();
        if (loaded.Contains(Guid.Empty)) throw new InvalidOperationException("Origem vazia no fechamento.");
        var keys = new HashSet<(Guid, Guid)>();
        var missing = new SortedSet<Guid>();
        foreach (var h in history)
        {
            if (h.ReferenceUuid == Guid.Empty || h.CompositionId == Guid.Empty ||
                !keys.Add((h.CompositionId, h.ReferenceUuid)) || h.MemberInitialUuids.IsDefaultOrEmpty ||
                h.MemberInitialUuids.Any(x => x == Guid.Empty) ||
                h.MemberInitialUuids.Distinct().Count() != h.MemberInitialUuids.Length)
                throw new InvalidOperationException("Histórico inválido ou duplicado no fechamento.");
            foreach (var id in h.MemberInitialUuids)
                if (!loaded.Contains(id)) missing.Add(id);
        }
        return missing.ToImmutableArray();
    }

    public static void RequireComplete(IEnumerable<IdentityCompositionHistory> history,
        IEnumerable<Guid> loadedInitialUuids)
    {
        if (!MissingMembers(history, loadedInitialUuids).IsEmpty)
            throw new InvalidOperationException("Leitura histórica incompleta.");
    }
}
