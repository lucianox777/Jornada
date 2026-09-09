using System.Collections.Immutable;
using Jornada.Contracts;
using Jornada.Operational.Sql;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class IdentityCompositionRecompositionPlanStoreTests
{
    private static readonly Guid Decision = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private const string RequestHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public void Canonical_recomposition_is_order_independent()
    {
        var first = Plan(
            ImmutableArray.Create(B, A),
            ImmutableArray.Create(B, A),
            ImmutableArray.Create(20L, 10L),
            ImmutableArray.Create(202L, 101L));
        var second = Plan(
            ImmutableArray.Create(A, B),
            ImmutableArray.Create(A, B),
            ImmutableArray.Create(10L, 20L),
            ImmutableArray.Create(101L, 202L));

        Assert.That(
            IdentityCompositionCanonical.SerializeRecompositionPlan(first),
            Is.EqualTo(IdentityCompositionCanonical.SerializeRecompositionPlan(second)));
    }

    [Test]
    public void Persisted_content_must_match_canonical_plan()
    {
        var plan = Plan(
            ImmutableArray.Create(A),
            ImmutableArray.Create(A, B),
            ImmutableArray.Create(10L),
            ImmutableArray.Create(101L));
        var json = IdentityCompositionCanonical.SerializeRecompositionPlan(plan);
        var hash = IdentityCompositionCanonical.HashUtf8(json);
        var receipt = new IdentityCompositionRecompositionPlanReceipt(
            Decision, RequestHash, IdentityCompositionRecompositionPlanner.Version,
            json, hash, DateTimeOffset.UnixEpoch, "PLANEJADA");

        Assert.DoesNotThrow(() => IdentityCompositionRecompositionPlanStore.ValidateContent(receipt, plan));
    }

    [Test]
    public void Divergent_persisted_hash_fails_closed()
    {
        var plan = Plan(
            ImmutableArray.Create(A),
            ImmutableArray.Create(A, B),
            ImmutableArray.Create(10L),
            ImmutableArray.Create(101L));
        var json = IdentityCompositionCanonical.SerializeRecompositionPlan(plan);
        var receipt = new IdentityCompositionRecompositionPlanReceipt(
            Decision, RequestHash, IdentityCompositionRecompositionPlanner.Version,
            json, new string('b', 64), DateTimeOffset.UnixEpoch, "PLANEJADA");

        Assert.Throws<InvalidOperationException>(() =>
            IdentityCompositionRecompositionPlanStore.ValidateContent(receipt, plan));
    }

    [Test]
    public void Wrong_recomposition_version_fails_closed()
    {
        var plan = Plan(
            ImmutableArray.Create(A),
            ImmutableArray.Create(A, B),
            ImmutableArray.Create(10L),
            ImmutableArray<long>.Empty);
        var json = IdentityCompositionCanonical.SerializeRecompositionPlan(plan);
        var receipt = new IdentityCompositionRecompositionPlanReceipt(
            Decision, RequestHash, "IDENTITY_COMPOSITION_RECOMPOSITION_PLAN_V0",
            json, IdentityCompositionCanonical.HashUtf8(json), DateTimeOffset.UnixEpoch, "PLANEJADA");

        Assert.Throws<InvalidOperationException>(() =>
            IdentityCompositionRecompositionPlanStore.ValidateContent(receipt, plan));
    }

    private static IdentityCompositionRecompositionPlan Plan(
        ImmutableArray<Guid> initials,
        ImmutableArray<Guid> references,
        ImmutableArray<long> sources,
        ImmutableArray<long> records) =>
        new(Decision, RequestHash, initials, references, sources, records, records.Length > 0);
}
