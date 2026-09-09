using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Contracts;

/// <summary>
/// Serialização canônica compartilhada entre planejador, ledger e adapter governado.
/// Não autentica conteúdo e não substitui autorização, leitura autoritativa ou locks.
/// </summary>
public static class IdentityCompositionCanonical
{
    public static string SerializeDecision(IdentityCompositionDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return JsonSerializer.Serialize(new
        {
            decision.DecisionId,
            decision.Operation,
            decision.EvidenceReference,
            decision.PolicyVersion,
            decision.DecidedAt,
            Assignments = decision.Assignments.OrderBy(a => a.InitialUuid).ToArray()
        });
    }

    public static string SerializePlan(IdentityCompositionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(plan);
    }

    public static string SerializeRecompositionPlan(IdentityCompositionRecompositionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.DecisionId == Guid.Empty || string.IsNullOrWhiteSpace(plan.CompositionRequestHash) ||
            plan.AffectedInitialUuids.IsDefault || plan.AffectedReferenceUuids.IsDefault ||
            plan.PessoaOrigemIds.IsDefault || plan.RegistroObservacaoIds.IsDefault)
            throw new InvalidOperationException("Plano de recomposição inválido para serialização canônica.");
        return JsonSerializer.Serialize(new
        {
            plan.DecisionId,
            plan.CompositionRequestHash,
            AffectedInitialUuids = plan.AffectedInitialUuids.Order().ToArray(),
            AffectedReferenceUuids = plan.AffectedReferenceUuids.Order().ToArray(),
            PessoaOrigemIds = plan.PessoaOrigemIds.Order().ToArray(),
            RegistroObservacaoIds = plan.RegistroObservacaoIds.Order().ToArray(),
            plan.RequiresFactualRevalidation
        });
    }

    public static string SerializeReservations(IEnumerable<Guid> reservations)
    {
        ArgumentNullException.ThrowIfNull(reservations);
        var values = reservations.Order().ToArray();
        if (values.Any(id => id == Guid.Empty) || values.Distinct().Count() != values.Length)
            throw new InvalidOperationException("Reservas inválidas ou duplicadas.");
        return JsonSerializer.Serialize(values);
    }

    public static string SerializeHistoryMembers(IEnumerable<Guid> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var values = members.Order().ToArray();
        if (values.Length == 0 || values.Any(id => id == Guid.Empty) || values.Distinct().Count() != values.Length)
            throw new InvalidOperationException("Membros históricos inválidos ou duplicados.");
        return JsonSerializer.Serialize(values);
    }

    public static string HashUtf8(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static string HashDecision(IdentityCompositionDecision decision) =>
        HashUtf8(SerializeDecision(decision));
}
