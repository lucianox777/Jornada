using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Contracts;

public enum IdentityCompositionOperation { FUSAO, SEPARACAO, REASSOCIACAO }
public enum HistoricalReferenceState { UNIVOCA, AMBIGUA, INDEFINIDA }

/// <summary>Snapshot de uma origem; CpfAnchorUuid é uma referência já admitida, nunca um CPF em claro.</summary>
public sealed record IdentityCompositionMember(
    Guid InitialUuid, Guid? CanonicalUuid, ProgressiveIdentityStatus Status,
    long Version, Guid? CpfAnchorUuid);

public sealed record IdentityCompositionAssignment(
    Guid InitialUuid, long ExpectedVersion, Guid? TargetUuid, ProgressiveIdentityStatus Status);

/// <summary>Conjunto de membros de uma referência numa versão histórica, não um redirecionamento mutável.</summary>
public sealed record IdentityCompositionHistory(
    Guid ReferenceUuid, Guid CompositionId, ImmutableArray<Guid> MemberInitialUuids);

/// <summary>Leitura autoritativa de um componente fechado. A completude deve ser provada pelo adapter transacional.</summary>
public sealed record IdentityCompositionReadSet(
    ImmutableArray<IdentityCompositionMember> Members,
    ImmutableArray<Guid> ReservedNewUuids,
    ImmutableArray<IdentityCompositionHistory> History);

public sealed record IdentityCompositionDecision(
    Guid DecisionId, IdentityCompositionOperation Operation,
    ImmutableArray<IdentityCompositionAssignment> Assignments,
    string EvidenceReference, string PolicyVersion, DateTimeOffset DecidedAt);

public sealed record IdentityCompositionChange(
    Guid InitialUuid, Guid? BeforeUuid, Guid? AfterUuid,
    ProgressiveIdentityStatus BeforeStatus, ProgressiveIdentityStatus AfterStatus,
    long ExpectedVersion, long NewVersion);

/// <summary>Plano puro. RequestHash identifica o conteúdo da decisão; não é recibo nem garantia de replay.</summary>
public sealed record IdentityCompositionPlan(
    Guid DecisionId, string RequestHash,
    ImmutableArray<IdentityCompositionChange> Changes,
    ImmutableArray<IdentityCompositionHistory> HistoryToAppend);

public sealed record HistoricalReferenceResolution(
    HistoricalReferenceState State, Guid? CanonicalUuid, ImmutableArray<Guid> Candidates);

/// <summary>Planejador puro. Não reserva UUID, persiste, autoriza, executa score ou altera fatos.</summary>
public static class IdentityCompositionPlanner
{
    public const string Version = "IDENTITY_COMPOSITION_PLAN_V1";

    public static IdentityCompositionPlan Prepare(IdentityCompositionReadSet readSet, IdentityCompositionDecision decision)
    {
        ArgumentNullException.ThrowIfNull(readSet);
        ArgumentNullException.ThrowIfNull(decision);
        ValidateReadSet(readSet);
        if (decision.DecisionId == Guid.Empty || !Enum.IsDefined(decision.Operation) ||
            decision.DecidedAt == default || decision.DecidedAt.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(decision.EvidenceReference) ||
            string.IsNullOrWhiteSpace(decision.PolicyVersion) || decision.Assignments.IsDefaultOrEmpty)
            throw new ArgumentException("Decisão de composição incompleta.", nameof(decision));

        // Idempotência e reutilização de decision_id pertencem ao ledger transacional futuro.
        // O histórico de agregados não é um ledger de decisões e pode ser vazio em certas operações.
        var members = readSet.Members.ToDictionary(m => m.InitialUuid);
        var assignments = Unique(decision.Assignments, a => a.InitialUuid);
        if (assignments.Count != decision.Assignments.Length || assignments.Keys.Any(id => !members.ContainsKey(id)))
            throw new InvalidOperationException("Partição contém origem duplicada ou desconhecida.");
        foreach (var a in assignments.Values)
        {
            var current = members[a.InitialUuid];
            if (a.ExpectedVersion != current.Version || current.Version == long.MaxValue)
                throw new InvalidOperationException("Versão obsoleta ou esgotada.");
            if (!Enum.IsDefined(a.Status) || a.TargetUuid == Guid.Empty ||
                (a.Status == ProgressiveIdentityStatus.REFERENCIA) != (a.TargetUuid is not null) ||
                a.Status == ProgressiveIdentityStatus.PROVISORIA)
                throw new InvalidOperationException("Destino ou estado inválido.");
        }

        // O adapter deve carregar/lockar todos os membros das referências envolvidas.
        // O planejador também recusa uma partição que deixe algum desses membros de fora.
        var currentGroups = readSet.Members.Where(m => m.CanonicalUuid is not null)
            .GroupBy(m => m.CanonicalUuid!.Value).ToDictionary(g => g.Key, g => g.ToArray());
        var affected = assignments.Values.Select(a => members[a.InitialUuid].CanonicalUuid)
            .Concat(assignments.Values.Select(a => a.TargetUuid))
            .Where(id => id is not null).Select(id => id!.Value).ToHashSet();
        foreach (var reference in affected)
            if (currentGroups.TryGetValue(reference, out var group) &&
                group.Any(m => !assignments.ContainsKey(m.InitialUuid)))
                throw new InvalidOperationException("Leitura/partição incompleta de agregado afetado.");

        var initialOwners = members.Values.ToDictionary(m => m.InitialUuid);
        var known = members.Values.Select(m => m.InitialUuid)
            .Concat(members.Values.Where(m => m.CanonicalUuid is not null).Select(m => m.CanonicalUuid!.Value))
            .Concat(members.Values.Where(m => m.CpfAnchorUuid is not null).Select(m => m.CpfAnchorUuid!.Value))
            .Concat(readSet.History.Select(h => h.ReferenceUuid)).ToHashSet();
        var reserved = readSet.ReservedNewUuids.ToHashSet();
        if (reserved.Any(id => known.Contains(id)))
            throw new InvalidOperationException("UUID reservado já possui identidade ou histórico.");

        var after = members.Values.ToDictionary(m => m.InitialUuid, m => m);
        foreach (var a in assignments.Values)
        {
            var before = members[a.InitialUuid];
            if (a.TargetUuid is { } target)
            {
                // Não reciclar um alias antigo como identidade nova, nem entregar o UUID
                // inicial de uma origem a um grupo que já não contém sua proprietária.
                if (!reserved.Contains(target) && !currentGroups.ContainsKey(target) &&
                    !(initialOwners.TryGetValue(target, out var owner) &&
                      assignments.TryGetValue(owner.InitialUuid, out var ownerAssignment) &&
                      ownerAssignment.TargetUuid == target))
                    throw new InvalidOperationException("Destino não é corrente, inicial próprio ou reserva nova.");
            }
            after[a.InitialUuid] = before with { CanonicalUuid = a.TargetUuid, Status = a.Status };
        }
        foreach (var group in after.Values.Where(m => m.CanonicalUuid is not null).GroupBy(m => m.CanonicalUuid!.Value))
        {
            var target = group.Key;
            var anchors = group.Where(m => m.CpfAnchorUuid is not null).Select(m => m.CpfAnchorUuid!.Value).Distinct().ToArray();
            if (anchors.Length > 1 || anchors.Any(anchor => anchor != target))
                throw new InvalidOperationException("Composição não pode transferir ou reunir âncoras CPF distintas.");
            if (initialOwners.TryGetValue(target, out var owner) && !group.Any(m => m.InitialUuid == owner.InitialUuid))
                throw new InvalidOperationException("UUID inicial não pode ser apropriado por outro agregado.");
        }
        foreach (var member in after.Values)
            if (member.CpfAnchorUuid is { } anchor && member.CanonicalUuid != anchor)
                throw new InvalidOperationException("Origem com âncora admitida deve preservar sua referência CPF.");

        var changes = assignments.Values.OrderBy(a => a.InitialUuid).Select(a =>
        {
            var before = members[a.InitialUuid];
            return new IdentityCompositionChange(a.InitialUuid, before.CanonicalUuid, a.TargetUuid,
                before.Status, a.Status, before.Version, checked(before.Version + 1));
        }).Where(c => c.BeforeUuid != c.AfterUuid || c.BeforeStatus != c.AfterStatus).ToImmutableArray();
        if (changes.IsEmpty) throw new InvalidOperationException("Decisão sem alteração; replay deve usar o recibo persistido.");

        var beforeGroups = currentGroups.Where(g => affected.Contains(g.Key)).ToArray();
        if (decision.Operation == IdentityCompositionOperation.FUSAO &&
            (beforeGroups.Length < 2 || assignments.Values.Select(a => a.TargetUuid).Distinct().Count() != 1 ||
             assignments.Values.Any(a => a.TargetUuid is null)))
            throw new InvalidOperationException("Fusão exige ao menos dois agregados e um destino explícito.");
        if (decision.Operation == IdentityCompositionOperation.SEPARACAO &&
            !beforeGroups.Any(g => g.Value.Select(m => after[m.InitialUuid].CanonicalUuid).Distinct().Count() > 1))
            throw new InvalidOperationException("Separação exige particionar um agregado existente.");

        // O snapshot anterior é preservado por membros, nunca por redirect transitivo.
        var history = beforeGroups.OrderBy(g => g.Key).Select(g => new IdentityCompositionHistory(
            g.Key, decision.DecisionId, g.Value.Select(m => m.InitialUuid).Order().ToImmutableArray())).ToImmutableArray();
        var hashPayload = new
        {
            decision.DecisionId, decision.Operation, decision.EvidenceReference, decision.PolicyVersion, decision.DecidedAt,
            Assignments = decision.Assignments.OrderBy(a => a.InitialUuid).ToArray()
        };
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(hashPayload)))).ToLowerInvariant();
        return new IdentityCompositionPlan(decision.DecisionId, hash, changes, history);
    }

    public static HistoricalReferenceResolution ResolveHistorical(
        IdentityCompositionHistory history, ImmutableArray<IdentityCompositionMember> current)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (current.IsDefault) throw new ArgumentException("Leitura ausente.", nameof(current));
        var members = Unique(current, m => m.InitialUuid);
        if (members.Count != current.Length)
            throw new InvalidOperationException("Leitura histórica contém UUID inicial duplicado.");
        ValidateMembers(members.Values);
        if (history.ReferenceUuid == Guid.Empty || history.CompositionId == Guid.Empty ||
            history.MemberInitialUuids.IsDefaultOrEmpty ||
            history.MemberInitialUuids.Any(id => id == Guid.Empty) ||
            history.MemberInitialUuids.Distinct().Count() != history.MemberInitialUuids.Length ||
            history.MemberInitialUuids.Any(id => !members.ContainsKey(id)))
            throw new InvalidOperationException("Histórico incompleto ou inconsistente.");
        var selected = history.MemberInitialUuids.Select(id => members[id]).ToArray();
        var candidates = selected.Where(m => m.CanonicalUuid is not null)
            .Select(m => m.CanonicalUuid!.Value).Distinct().Order().ToImmutableArray();
        var unresolved = selected.Any(m => m.CanonicalUuid is null);
        var state = candidates.Length > 1 ? HistoricalReferenceState.AMBIGUA :
            unresolved || candidates.IsEmpty ? HistoricalReferenceState.INDEFINIDA : HistoricalReferenceState.UNIVOCA;
        return new HistoricalReferenceResolution(state,
            state == HistoricalReferenceState.UNIVOCA ? candidates[0] : null, candidates);
    }

    private static void ValidateReadSet(IdentityCompositionReadSet readSet)
    {
        if (readSet.Members.IsDefaultOrEmpty || readSet.ReservedNewUuids.IsDefault || readSet.History.IsDefault)
            throw new InvalidOperationException("Leitura de composição ausente.");
        var members = Unique(readSet.Members, m => m.InitialUuid);
        if (members.Count != readSet.Members.Length || members.ContainsKey(Guid.Empty))
            throw new InvalidOperationException("UUID inicial duplicado ou vazio.");
        ValidateMembers(members.Values);
        if (readSet.ReservedNewUuids.Any(id => id == Guid.Empty) ||
            readSet.ReservedNewUuids.Distinct().Count() != readSet.ReservedNewUuids.Length)
            throw new InvalidOperationException("Reservas inválidas ou duplicadas.");
        var historyKeys = new HashSet<(Guid, Guid)>();
        foreach (var h in readSet.History)
            if (h.ReferenceUuid == Guid.Empty || h.CompositionId == Guid.Empty ||
                !historyKeys.Add((h.ReferenceUuid, h.CompositionId)) || h.MemberInitialUuids.IsDefaultOrEmpty ||
                h.MemberInitialUuids.Any(id => id == Guid.Empty || !members.ContainsKey(id)) ||
                h.MemberInitialUuids.Distinct().Count() != h.MemberInitialUuids.Length)
                throw new InvalidOperationException("Histórico duplicado ou incompleto.");
    }

    private static void ValidateMembers(IEnumerable<IdentityCompositionMember> members)
    {
        foreach (var m in members)
            if (m.InitialUuid == Guid.Empty || m.CanonicalUuid == Guid.Empty || m.CpfAnchorUuid == Guid.Empty || m.Version < 0 ||
                !Enum.IsDefined(m.Status) ||
                (m.Status == ProgressiveIdentityStatus.REFERENCIA) != (m.CanonicalUuid is not null) ||
                (m.Status == ProgressiveIdentityStatus.PROVISORIA && m.Version != 0) ||
                (m.Status != ProgressiveIdentityStatus.PROVISORIA && m.Version == 0))
                throw new InvalidOperationException("Snapshot progressivo inválido.");
    }

    private static Dictionary<TKey, TValue> Unique<TKey, TValue>(IEnumerable<TValue> values, Func<TValue, TKey> key)
        where TKey : notnull => values.GroupBy(key).ToDictionary(g => g.Key, g => g.First());
}
