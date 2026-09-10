using System.Collections.Immutable;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class IdentityCompositionPublicationTests
{
    private static readonly Guid Decision = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public void Assigned_uuid_is_preserved_from_factual_revalidation_only()
    {
        var command = IdentityCompositionPublicationPlanner.Prepare(Plan(
            new IdentityCompositionFactualProjectionExpectation(A, 101, B, "ATRIBUIDA")));

        Assert.That(command.DecisionId, Is.EqualTo(Decision));
        Assert.That(command.RecompositionPlanHash, Is.EqualTo(Hash));
        Assert.That(command.FactualRevalidationVersion, Is.EqualTo(IdentityCompositionFactualRevalidationPlanner.Version));
        Assert.That(command.Mutations.Single().PessoaUuid, Is.EqualTo(B));
        Assert.That(command.Mutations.Single().AssignmentState, Is.EqualTo("ATRIBUIDA"));
    }

    [TestCase("PENDENTE_IDENTIDADE")]
    [TestCase("CONFLITO_IDENTIDADE")]
    public void Non_assigned_states_never_carry_uuid(string state)
    {
        var command = IdentityCompositionPublicationPlanner.Prepare(Plan(
            new IdentityCompositionFactualProjectionExpectation(A, 101, null, state)));

        Assert.That(command.Mutations.Single().PessoaUuid, Is.Null);
        Assert.That(command.Mutations.Single().AssignmentState, Is.EqualTo(state));
    }

    [Test]
    public void Duplicate_record_fails_closed()
    {
        var plan = Plan(
            new IdentityCompositionFactualProjectionExpectation(A, 101, null, "PENDENTE_IDENTIDADE"),
            new IdentityCompositionFactualProjectionExpectation(B, 101, null, "CONFLITO_IDENTIDADE"));

        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPublicationPlanner.Prepare(plan));
    }

    [Test]
    public void Assigned_without_uuid_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPublicationPlanner.Prepare(Plan(
            new IdentityCompositionFactualProjectionExpectation(A, 101, null, "ATRIBUIDA"))));
    }

    [Test]
    public void Non_assigned_with_uuid_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPublicationPlanner.Prepare(Plan(
            new IdentityCompositionFactualProjectionExpectation(A, 101, B, "PENDENTE_IDENTIDADE"))));
    }

    [Test]
    public void Unknown_assignment_state_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPublicationPlanner.Prepare(Plan(
            new IdentityCompositionFactualProjectionExpectation(A, 101, null, "DESCONHECIDA"))));
    }

    [Test]
    public void Non_publishable_revalidation_fails_closed()
    {
        var plan = new IdentityCompositionFactualRevalidationPlan(
            Decision, Hash, ImmutableArray<IdentityCompositionFactualProjectionExpectation>.Empty, false);

        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPublicationPlanner.Prepare(plan));
    }

    [Test]
    public void Empty_closed_scope_is_valid()
    {
        var command = IdentityCompositionPublicationPlanner.Prepare(Plan());

        Assert.That(command.Mutations, Is.Empty);
    }

    private static IdentityCompositionFactualRevalidationPlan Plan(
        params IdentityCompositionFactualProjectionExpectation[] expectations) =>
        new(Decision, Hash, expectations.ToImmutableArray(), true);
}
