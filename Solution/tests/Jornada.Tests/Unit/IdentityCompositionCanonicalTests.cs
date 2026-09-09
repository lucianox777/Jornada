using System.Collections.Immutable;
using System.Text.Json;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class IdentityCompositionCanonicalTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid R1 = Guid.Parse("00000000-0000-0000-0000-000000000101");
    private static readonly Guid R2 = Guid.Parse("00000000-0000-0000-0000-000000000102");
    private static readonly DateTimeOffset When = new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);

    private static IdentityCompositionDecision Decision(ImmutableArray<IdentityCompositionAssignment> assignments) =>
        new(Guid.Parse("00000000-0000-0000-0000-000000000201"), IdentityCompositionOperation.FUSAO,
            assignments, "evidence:synthetic", "COMPOSITION_CANONICAL_V1", When);

    [Test]
    public void Decision_serialization_matches_planner_hash_and_is_assignment_order_independent()
    {
        var forward = Decision(ImmutableArray.Create(
            new IdentityCompositionAssignment(A, 1, A, ProgressiveIdentityStatus.REFERENCIA),
            new IdentityCompositionAssignment(B, 1, A, ProgressiveIdentityStatus.REFERENCIA)));
        var reverse = forward with { Assignments = forward.Assignments.Reverse().ToImmutableArray() };
        var read = new IdentityCompositionReadSet(
            ImmutableArray.Create(
                new IdentityCompositionMember(A, A, ProgressiveIdentityStatus.REFERENCIA, 1, null),
                new IdentityCompositionMember(B, B, ProgressiveIdentityStatus.REFERENCIA, 1, null)),
            ImmutableArray<Guid>.Empty,
            ImmutableArray<IdentityCompositionHistory>.Empty);

        var plan = IdentityCompositionPlanner.Prepare(read, forward);

        Assert.Multiple(() =>
        {
            Assert.That(IdentityCompositionCanonical.SerializeDecision(reverse),
                Is.EqualTo(IdentityCompositionCanonical.SerializeDecision(forward)));
            Assert.That(IdentityCompositionCanonical.HashDecision(forward), Is.EqualTo(plan.RequestHash));
            Assert.That(IdentityCompositionCanonical.HashDecision(reverse), Is.EqualTo(plan.RequestHash));
        });
    }

    [Test]
    public void Reservation_serialization_is_stable_and_rejects_invalid_sets()
    {
        var forward = IdentityCompositionCanonical.SerializeReservations(new[] { R1, R2 });
        var reverse = IdentityCompositionCanonical.SerializeReservations(new[] { R2, R1 });

        Assert.Multiple(() =>
        {
            Assert.That(reverse, Is.EqualTo(forward));
            Assert.That(JsonSerializer.Deserialize<Guid[]>(forward), Is.EqualTo(new[] { R1, R2 }));
            Assert.Throws<InvalidOperationException>(() =>
                IdentityCompositionCanonical.SerializeReservations(new[] { R1, R1 }));
            Assert.Throws<InvalidOperationException>(() =>
                IdentityCompositionCanonical.SerializeReservations(new[] { Guid.Empty }));
        });
    }

    [Test]
    public void Plan_serialization_preserves_exact_typed_content()
    {
        var decision = Decision(ImmutableArray.Create(
            new IdentityCompositionAssignment(A, 1, A, ProgressiveIdentityStatus.REFERENCIA),
            new IdentityCompositionAssignment(B, 1, A, ProgressiveIdentityStatus.REFERENCIA)));
        var read = new IdentityCompositionReadSet(
            ImmutableArray.Create(
                new IdentityCompositionMember(A, A, ProgressiveIdentityStatus.REFERENCIA, 1, null),
                new IdentityCompositionMember(B, B, ProgressiveIdentityStatus.REFERENCIA, 1, null)),
            ImmutableArray<Guid>.Empty,
            ImmutableArray<IdentityCompositionHistory>.Empty);
        var plan = IdentityCompositionPlanner.Prepare(read, decision);
        var json = IdentityCompositionCanonical.SerializePlan(plan);
        var roundTrip = JsonSerializer.Deserialize<IdentityCompositionPlan>(json)
            ?? throw new InvalidOperationException("Round-trip do plano retornou null.");

        Assert.Multiple(() =>
        {
            Assert.That(roundTrip.DecisionId, Is.EqualTo(plan.DecisionId));
            Assert.That(roundTrip.RequestHash, Is.EqualTo(plan.RequestHash));
            Assert.That(roundTrip.Changes, Is.EqualTo(plan.Changes));
            Assert.That(roundTrip.HistoryToAppend, Is.EqualTo(plan.HistoryToAppend));
            Assert.That(IdentityCompositionCanonical.SerializePlan(roundTrip), Is.EqualTo(json));
        });
    }
}
