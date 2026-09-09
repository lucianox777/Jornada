using System.Collections.Immutable;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class IdentityCompositionFactualRevalidationTests
{
    private static readonly Guid Decision = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public void Resolved_link_is_the_only_source_of_assigned_uuid()
    {
        var plan = IdentityCompositionFactualRevalidationPlanner.Prepare(
            Recomposition(A, 101), Hash,
            ImmutableArray.Create(Snapshot(A, 101, C, "RESOLVIDO")));

        Assert.That(plan.IsPublishable, Is.True);
        Assert.That(plan.Expectations.Single().PessoaUuid, Is.EqualTo(C));
        Assert.That(plan.Expectations.Single().AssignmentState, Is.EqualTo("ATRIBUIDA"));
    }

    [TestCase("CONFLITO")]
    [TestCase("CONFLITO_GOVERNADO")]
    public void Conflict_is_projected_without_uuid(string status)
    {
        var plan = IdentityCompositionFactualRevalidationPlanner.Prepare(
            Recomposition(A, 101), Hash,
            ImmutableArray.Create(Snapshot(A, 101, null, status)));

        Assert.That(plan.Expectations.Single().PessoaUuid, Is.Null);
        Assert.That(plan.Expectations.Single().AssignmentState, Is.EqualTo("CONFLITO_IDENTIDADE"));
    }

    [Test]
    public void Missing_or_unresolved_link_is_pending()
    {
        var recomposition = Recomposition(A, 101, 102);
        var snapshots = ImmutableArray.Create(
            Snapshot(A, 101, null, null),
            Snapshot(A, 102, null, "NAO_RESOLVIDO"));

        var plan = IdentityCompositionFactualRevalidationPlanner.Prepare(recomposition, Hash, snapshots);

        Assert.That(plan.Expectations.Select(x => x.AssignmentState),
            Is.EqualTo(new[] { "PENDENTE_IDENTIDADE", "PENDENTE_IDENTIDADE" }));
    }

    [Test]
    public void Structural_reference_is_not_used_as_factual_assignment()
    {
        var recomposition = new IdentityCompositionRecompositionPlan(
            Decision, "request", ImmutableArray.Create(A), ImmutableArray.Create(B),
            ImmutableArray.Create(10L), ImmutableArray.Create(101L), true);

        var plan = IdentityCompositionFactualRevalidationPlanner.Prepare(
            recomposition, Hash, ImmutableArray.Create(Snapshot(A, 101, null, null)));

        Assert.That(plan.Expectations.Single().PessoaUuid, Is.Null);
        Assert.That(plan.Expectations.Single().AssignmentState, Is.EqualTo("PENDENTE_IDENTIDADE"));
    }

    [Test]
    public void Missing_record_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionFactualRevalidationPlanner.Prepare(
            Recomposition(A, 101, 102), Hash,
            ImmutableArray.Create(Snapshot(A, 101, null, null))));
    }

    [Test]
    public void Extra_or_wrong_origin_record_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionFactualRevalidationPlanner.Prepare(
            Recomposition(A, 101), Hash,
            ImmutableArray.Create(Snapshot(B, 101, null, null))));
    }

    [Test]
    public void Resolved_without_uuid_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionFactualRevalidationPlanner.Prepare(
            Recomposition(A, 101), Hash,
            ImmutableArray.Create(Snapshot(A, 101, null, "RESOLVIDO"))));
    }

    [Test]
    public void Non_resolved_with_uuid_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionFactualRevalidationPlanner.Prepare(
            Recomposition(A, 101), Hash,
            ImmutableArray.Create(Snapshot(A, 101, C, "NAO_RESOLVIDO"))));
    }

    [Test]
    public void No_facts_is_a_valid_empty_revalidation()
    {
        var recomposition = new IdentityCompositionRecompositionPlan(
            Decision, "request", ImmutableArray.Create(A), ImmutableArray.Create(A, B),
            ImmutableArray.Create(10L), ImmutableArray<long>.Empty, false);

        var plan = IdentityCompositionFactualRevalidationPlanner.Prepare(
            recomposition, Hash, ImmutableArray<IdentityCompositionFactualAttributionSnapshot>.Empty);

        Assert.That(plan.Expectations, Is.Empty);
        Assert.That(plan.IsPublishable, Is.True);
    }

    private static IdentityCompositionRecompositionPlan Recomposition(Guid initial, params long[] records) =>
        new(Decision, "request", ImmutableArray.Create(initial), ImmutableArray.Create(initial, B),
            ImmutableArray.Create(10L), records.ToImmutableArray(), records.Length > 0);

    private static IdentityCompositionFactualAttributionSnapshot Snapshot(
        Guid initial, long record, Guid? uuid, string? status) =>
        new(initial, 10, 20, record, uuid, status);
}
