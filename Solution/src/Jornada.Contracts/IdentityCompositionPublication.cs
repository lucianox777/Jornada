using System.Collections.Immutable;

namespace Jornada.Contracts;

public sealed record IdentityCompositionPublicationMutation(
    Guid InitialUuid,
    long RegistroObservacaoId,
    Guid? PessoaUuid,
    string AssignmentState);

/// <summary>
/// Comando fechado para a futura fronteira transacional de publicação. Ele contém somente a
/// atribuição factual previamente revalidada; referências estruturais da composição não fazem
/// parte do contrato e, portanto, não podem ser usadas como destino implícito de fatos.
/// </summary>
public sealed record IdentityCompositionPublicationCommand(
    Guid DecisionId,
    string RecompositionPlanHash,
    string FactualRevalidationVersion,
    ImmutableArray<IdentityCompositionPublicationMutation> Mutations);

public static class IdentityCompositionPublicationPlanner
{
    public const string Version = "IDENTITY_COMPOSITION_PUBLICATION_V1";

    public static IdentityCompositionPublicationCommand Prepare(
        IdentityCompositionFactualRevalidationPlan factualPlan)
    {
        ArgumentNullException.ThrowIfNull(factualPlan);
        if (factualPlan.DecisionId == Guid.Empty ||
            !IsHash(factualPlan.RecompositionPlanHash) ||
            !factualPlan.IsPublishable ||
            factualPlan.Expectations.IsDefault)
            throw new InvalidOperationException("Revalidação factual não autoriza publicação.");

        var seenRecords = new HashSet<long>();
        var mutations = ImmutableArray.CreateBuilder<IdentityCompositionPublicationMutation>(factualPlan.Expectations.Length);
        foreach (var expectation in factualPlan.Expectations.OrderBy(x => x.RegistroObservacaoId))
        {
            if (expectation.InitialUuid == Guid.Empty || expectation.RegistroObservacaoId <= 0 ||
                !seenRecords.Add(expectation.RegistroObservacaoId))
                throw new InvalidOperationException("Mutação factual inválida ou duplicada.");

            ValidateAssignment(expectation.PessoaUuid, expectation.AssignmentState);
            mutations.Add(new IdentityCompositionPublicationMutation(
                expectation.InitialUuid,
                expectation.RegistroObservacaoId,
                expectation.PessoaUuid,
                expectation.AssignmentState));
        }

        return new IdentityCompositionPublicationCommand(
            factualPlan.DecisionId,
            factualPlan.RecompositionPlanHash,
            IdentityCompositionFactualRevalidationPlanner.Version,
            mutations.ToImmutable());
    }

    private static void ValidateAssignment(Guid? pessoaUuid, string assignmentState)
    {
        if (string.Equals(assignmentState, "ATRIBUIDA", StringComparison.Ordinal))
        {
            if (!pessoaUuid.HasValue || pessoaUuid.Value == Guid.Empty)
                throw new InvalidOperationException("ATRIBUIDA exige UUID factual autoritativo.");
            return;
        }

        if (string.Equals(assignmentState, "PENDENTE_IDENTIDADE", StringComparison.Ordinal) ||
            string.Equals(assignmentState, "CONFLITO_IDENTIDADE", StringComparison.Ordinal))
        {
            if (pessoaUuid is not null)
                throw new InvalidOperationException("Estado factual não atribuído não pode carregar UUID.");
            return;
        }

        throw new InvalidOperationException("Estado de atribuição factual desconhecido.");
    }

    private static bool IsHash(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
