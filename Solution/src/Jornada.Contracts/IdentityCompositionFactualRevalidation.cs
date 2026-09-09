using System.Collections.Immutable;

namespace Jornada.Contracts;

public sealed record IdentityCompositionFactualAttributionSnapshot(
    Guid InitialUuid,
    long PessoaOrigemId,
    long PessoaObservacaoId,
    long RegistroObservacaoId,
    Guid? AuthoritativePessoaUuid,
    string? LinkStatus);

public sealed record IdentityCompositionFactualProjectionExpectation(
    Guid InitialUuid,
    long RegistroObservacaoId,
    Guid? PessoaUuid,
    string AssignmentState);

/// <summary>
/// Plano puro de revalidação factual anterior à publicação. A composição estrutural define apenas
/// o escopo; a atribuição esperada vem exclusivamente do vínculo factual corrente já autoritativo.
/// </summary>
public sealed record IdentityCompositionFactualRevalidationPlan(
    Guid DecisionId,
    string RecompositionPlanHash,
    ImmutableArray<IdentityCompositionFactualProjectionExpectation> Expectations,
    bool IsPublishable);

public static class IdentityCompositionFactualRevalidationPlanner
{
    public const string Version = "IDENTITY_COMPOSITION_FACTUAL_REVALIDATION_V1";

    public static IdentityCompositionFactualRevalidationPlan Prepare(
        IdentityCompositionRecompositionPlan recomposition,
        string recompositionPlanHash,
        ImmutableArray<IdentityCompositionFactualAttributionSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(recomposition);
        if (recomposition.DecisionId == Guid.Empty || !IsHash(recompositionPlanHash) ||
            recomposition.AffectedInitialUuids.IsDefault || recomposition.RegistroObservacaoIds.IsDefault)
            throw new InvalidOperationException("Plano de recomposição ausente ou inválido.");
        if (snapshots.IsDefault)
            throw new InvalidOperationException("Leitura factual autoritativa ausente.");

        var affected = recomposition.AffectedInitialUuids.ToHashSet();
        if (affected.Count != recomposition.AffectedInitialUuids.Length || affected.Contains(Guid.Empty))
            throw new InvalidOperationException("UUIDs iniciais afetados inválidos ou duplicados.");
        var expectedRecords = recomposition.RegistroObservacaoIds.ToHashSet();
        if (expectedRecords.Count != recomposition.RegistroObservacaoIds.Length || expectedRecords.Any(id => id <= 0))
            throw new InvalidOperationException("Registros afetados inválidos ou duplicados.");

        if (snapshots.Length != expectedRecords.Count)
            throw new InvalidOperationException("Leitura factual não fecha exatamente os registros afetados.");
        var seenRecords = new HashSet<long>();
        var expectations = ImmutableArray.CreateBuilder<IdentityCompositionFactualProjectionExpectation>(snapshots.Length);
        foreach (var snapshot in snapshots.OrderBy(x => x.RegistroObservacaoId))
        {
            if (snapshot.InitialUuid == Guid.Empty || !affected.Contains(snapshot.InitialUuid) ||
                snapshot.PessoaOrigemId <= 0 || snapshot.PessoaObservacaoId <= 0 || snapshot.RegistroObservacaoId <= 0 ||
                !expectedRecords.Contains(snapshot.RegistroObservacaoId) || !seenRecords.Add(snapshot.RegistroObservacaoId))
                throw new InvalidOperationException("Snapshot factual está fora do escopo fechado.");

            var status = snapshot.LinkStatus;
            Guid? uuid;
            string state;
            if (string.Equals(status, "RESOLVIDO", StringComparison.Ordinal))
            {
                if (!snapshot.AuthoritativePessoaUuid.HasValue || snapshot.AuthoritativePessoaUuid.Value == Guid.Empty)
                    throw new InvalidOperationException("Vínculo RESOLVIDO exige UUID autoritativo.");
                uuid = snapshot.AuthoritativePessoaUuid;
                state = "ATRIBUIDA";
            }
            else if (string.Equals(status, "CONFLITO", StringComparison.Ordinal) ||
                     string.Equals(status, "CONFLITO_GOVERNADO", StringComparison.Ordinal))
            {
                if (snapshot.AuthoritativePessoaUuid is not null)
                    throw new InvalidOperationException("Vínculo em conflito não pode expor UUID autoritativo.");
                uuid = null;
                state = "CONFLITO_IDENTIDADE";
            }
            else
            {
                if (snapshot.AuthoritativePessoaUuid is not null)
                    throw new InvalidOperationException("Vínculo não resolvido não pode expor UUID autoritativo.");
                uuid = null;
                state = "PENDENTE_IDENTIDADE";
            }

            expectations.Add(new IdentityCompositionFactualProjectionExpectation(
                snapshot.InitialUuid, snapshot.RegistroObservacaoId, uuid, state));
        }

        if (!seenRecords.SetEquals(expectedRecords))
            throw new InvalidOperationException("Leitura factual deixou registro afetado sem cobertura.");

        return new IdentityCompositionFactualRevalidationPlan(
            recomposition.DecisionId,
            recompositionPlanHash,
            expectations.ToImmutable(),
            IsPublishable: true);
    }

    private static bool IsHash(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
