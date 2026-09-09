using System.Collections.Immutable;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class IdentityCompositionRecompositionTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Test]
    public void Complete_scope_produces_deterministic_impact_without_reassignment()
    {
        var composition = Plan(
            Change(A, A, C),
            Change(B, B, C));
        var scopes = ImmutableArray.Create(
            Scope(B, 20, 202, 201),
            Scope(A, 10, 102, 101));

        var result = IdentityCompositionRecompositionPlanner.Prepare(composition, scopes);

        Assert.That(result.DecisionId, Is.EqualTo(composition.DecisionId));
        Assert.That(result.CompositionRequestHash, Is.EqualTo("request-hash"));
        Assert.That(result.AffectedInitialUuids, Is.EqualTo(new[] { A, B }));
        Assert.That(result.AffectedReferenceUuids, Is.EqualTo(new[] { A, B, C }));
        Assert.That(result.PessoaOrigemIds, Is.EqualTo(new long[] { 10, 20 }));
        Assert.That(result.RegistroObservacaoIds, Is.EqualTo(new long[] { 101, 102, 201, 202 }));
        Assert.That(result.RequiresFactualRevalidation, Is.True);
    }

    [Test]
    public void Missing_changed_origin_fails_closed()
    {
        var composition = Plan(Change(A, A, C), Change(B, B, C));
        var scopes = ImmutableArray.Create(Scope(A, 10, 101));

        Assert.Throws<InvalidOperationException>(() =>
            IdentityCompositionRecompositionPlanner.Prepare(composition, scopes));
    }

    [Test]
    public void Extra_origin_fails_closed()
    {
        var composition = Plan(Change(A, A, C));
        var scopes = ImmutableArray.Create(Scope(A, 10, 101), Scope(B, 20, 201));

        Assert.Throws<InvalidOperationException>(() =>
            IdentityCompositionRecompositionPlanner.Prepare(composition, scopes));
    }

    [Test]
    public void Incomplete_scope_fails_closed()
    {
        var composition = Plan(Change(A, A, C));
        var scopes = ImmutableArray.Create(new IdentityCompositionProjectionScope(
            A, 10, ImmutableArray.Create(101L), IsComplete: false));

        Assert.Throws<InvalidOperationException>(() =>
            IdentityCompositionRecompositionPlanner.Prepare(composition, scopes));
    }

    [Test]
    public void Duplicate_fact_across_origins_fails_closed()
    {
        var composition = Plan(Change(A, A, C), Change(B, B, C));
        var scopes = ImmutableArray.Create(Scope(A, 10, 101), Scope(B, 20, 101));

        Assert.Throws<InvalidOperationException>(() =>
            IdentityCompositionRecompositionPlanner.Prepare(composition, scopes));
    }

    [Test]
    public void Duplicate_pessoa_origem_fails_closed()
    {
        var composition = Plan(Change(A, A, C), Change(B, B, C));
        var scopes = ImmutableArray.Create(Scope(A, 10, 101), Scope(B, 10, 201));

        Assert.Throws<InvalidOperationException>(() =>
            IdentityCompositionRecompositionPlanner.Prepare(composition, scopes));
    }

    [Test]
    public void Complete_origin_without_facts_is_valid_and_does_not_invent_work()
    {
        var composition = Plan(Change(A, A, C));
        var scopes = ImmutableArray.Create(new IdentityCompositionProjectionScope(
            A, 10, ImmutableArray<long>.Empty, IsComplete: true));

        var result = IdentityCompositionRecompositionPlanner.Prepare(composition, scopes);

        Assert.That(result.RegistroObservacaoIds, Is.Empty);
        Assert.That(result.RequiresFactualRevalidation, Is.False);
        Assert.That(result.AffectedReferenceUuids, Is.EqualTo(new[] { A, C }));
    }

    private static IdentityCompositionPlan Plan(params IdentityCompositionChange[] changes) =>
        new(Guid.Parse("10000000-0000-0000-0000-000000000001"), "request-hash",
            changes.ToImmutableArray(), ImmutableArray<IdentityCompositionHistory>.Empty);

    private static IdentityCompositionChange Change(Guid initial, Guid? before, Guid? after) =>
        new(initial, before, after, ProgressiveIdentityStatus.REFERENCIA,
            after is null ? ProgressiveIdentityStatus.INDEFINIDA : ProgressiveIdentityStatus.REFERENCIA,
            1, 2);

    private static IdentityCompositionProjectionScope Scope(Guid initial, long pessoaOrigemId, params long[] registros) =>
        new(initial, pessoaOrigemId, registros.ToImmutableArray(), IsComplete: true);
}
