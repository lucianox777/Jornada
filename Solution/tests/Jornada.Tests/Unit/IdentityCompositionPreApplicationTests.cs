using System.Collections.Immutable;
using Jornada.Contracts;
using Jornada.Operational.Sql;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class IdentityCompositionPreApplicationTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid DecisionId = Guid.Parse("00000000-0000-0000-0000-000000000201");
    private static readonly DateTimeOffset When = new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);

    private static IdentityCompositionReadSet Read(
        IdentityCompositionMember a,
        IdentityCompositionMember b) =>
        new(ImmutableArray.Create(a, b), ImmutableArray<Guid>.Empty,
            ImmutableArray<IdentityCompositionHistory>.Empty);

    private static IdentityCompositionMember Member(Guid initial, Guid? canonical, long version = 1, Guid? anchor = null) =>
        new(initial, canonical,
            canonical is null ? ProgressiveIdentityStatus.INDEFINIDA : ProgressiveIdentityStatus.REFERENCIA,
            version, anchor);

    private static IdentityCompositionDecision Decision() =>
        new(DecisionId, IdentityCompositionOperation.FUSAO,
            ImmutableArray.Create(
                new IdentityCompositionAssignment(A, 1, A, ProgressiveIdentityStatus.REFERENCIA),
                new IdentityCompositionAssignment(B, 1, A, ProgressiveIdentityStatus.REFERENCIA)),
            "evidence:synthetic", "PREAPPLICATION_TEST_V1", When);

    private static IdentityCompositionPreparedReceipt Receipt(
        IdentityCompositionDecision decision,
        IdentityCompositionPlan plan,
        IEnumerable<Guid> reservations)
    {
        var request = IdentityCompositionCanonical.SerializeDecision(decision);
        var planJson = IdentityCompositionCanonical.SerializePlan(plan);
        var reservationJson = IdentityCompositionCanonical.SerializeReservations(reservations);
        return new IdentityCompositionPreparedReceipt(
            decision.DecisionId,
            IdentityCompositionCanonical.HashUtf8(request),
            IdentityCompositionCanonical.HashUtf8(planJson),
            IdentityCompositionCanonical.HashUtf8(reservationJson),
            request, planJson, reservationJson,
            "synthetic:preapplication", null, "PREPARADA");
    }

    [Test]
    public void Unchanged_authoritative_state_reproduces_prepared_plan_exactly()
    {
        var read = Read(Member(A, A), Member(B, B));
        var decision = Decision();
        var plan = IdentityCompositionPlanner.Prepare(read, decision);
        var receipt = Receipt(decision, plan, Array.Empty<Guid>());

        var replanned = IdentityCompositionPreApplicationService.ValidateAuthoritativeState(
            receipt, decision, plan, Array.Empty<Guid>(), read);

        Assert.That(IdentityCompositionCanonical.SerializePlan(replanned), Is.EqualTo(receipt.PlanJson));
    }

    [Test]
    public void Version_change_after_preparation_is_rejected()
    {
        var preparedRead = Read(Member(A, A), Member(B, B));
        var decision = Decision();
        var plan = IdentityCompositionPlanner.Prepare(preparedRead, decision);
        var receipt = Receipt(decision, plan, Array.Empty<Guid>());
        var live = Read(Member(A, A, 2), Member(B, B));

        Assert.Throws<InvalidOperationException>(() =>
            IdentityCompositionPreApplicationService.ValidateAuthoritativeState(
                receipt, decision, plan, Array.Empty<Guid>(), live));
    }

    [Test]
    public void Changed_current_reference_after_preparation_is_rejected_as_obsolete()
    {
        var preparedRead = Read(Member(A, A), Member(B, B));
        var decision = Decision();
        var plan = IdentityCompositionPlanner.Prepare(preparedRead, decision);
        var receipt = Receipt(decision, plan, Array.Empty<Guid>());
        var live = Read(Member(A, A), Member(B, C));

        Assert.Throws<InvalidOperationException>(() =>
            IdentityCompositionPreApplicationService.ValidateAuthoritativeState(
                receipt, decision, plan, Array.Empty<Guid>(), live));
    }

    [Test]
    public void Missing_member_of_affected_aggregate_fails_closed()
    {
        var preparedRead = Read(Member(A, A), Member(B, B));
        var decision = Decision();
        var plan = IdentityCompositionPlanner.Prepare(preparedRead, decision);
        var receipt = Receipt(decision, plan, Array.Empty<Guid>());
        var live = new IdentityCompositionReadSet(
            ImmutableArray.Create(Member(A, A), Member(B, A), Member(C, A)),
            ImmutableArray<Guid>.Empty,
            ImmutableArray<IdentityCompositionHistory>.Empty);

        Assert.Throws<InvalidOperationException>(() =>
            IdentityCompositionPreApplicationService.ValidateAuthoritativeState(
                receipt, decision, plan, Array.Empty<Guid>(), live));
    }

    [Test]
    public void Newly_observed_distinct_cpf_anchor_blocks_prepared_merge()
    {
        var preparedRead = Read(Member(A, A), Member(B, B));
        var decision = Decision();
        var plan = IdentityCompositionPlanner.Prepare(preparedRead, decision);
        var receipt = Receipt(decision, plan, Array.Empty<Guid>());
        var live = Read(Member(A, A, anchor: A), Member(B, B, anchor: B));

        Assert.Throws<InvalidOperationException>(() =>
            IdentityCompositionPreApplicationService.ValidateAuthoritativeState(
                receipt, decision, plan, Array.Empty<Guid>(), live));
    }

    [Test]
    public void Reservation_set_must_still_match_prepared_receipt_exactly()
    {
        var preparedRead = Read(Member(A, A), Member(B, B));
        var decision = Decision();
        var plan = IdentityCompositionPlanner.Prepare(preparedRead, decision);
        var receipt = Receipt(decision, plan, Array.Empty<Guid>());

        Assert.Throws<InvalidOperationException>(() =>
            IdentityCompositionPreApplicationService.ValidateAuthoritativeState(
                receipt, decision, plan, new[] { C }, preparedRead));
    }
}
