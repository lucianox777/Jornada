using System.Collections.Immutable;
using Jornada.Contracts;
using Jornada.Operational.Sql;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class IdentityCompositionLedgerStoreTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid R = Guid.Parse("00000000-0000-0000-0000-000000000101");
    private static readonly Guid DecisionId = Guid.Parse("00000000-0000-0000-0000-000000000201");
    private static readonly DateTimeOffset When = new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);

    private static (IdentityCompositionDecision Decision, IdentityCompositionPlan Plan) Build()
    {
        var read = new IdentityCompositionReadSet(
            ImmutableArray.Create(
                new IdentityCompositionMember(A, A, ProgressiveIdentityStatus.REFERENCIA, 1, null),
                new IdentityCompositionMember(B, B, ProgressiveIdentityStatus.REFERENCIA, 1, null)),
            ImmutableArray.Create(R),
            ImmutableArray<IdentityCompositionHistory>.Empty);
        var decision = new IdentityCompositionDecision(
            DecisionId,
            IdentityCompositionOperation.FUSAO,
            ImmutableArray.Create(
                new IdentityCompositionAssignment(A, 1, A, ProgressiveIdentityStatus.REFERENCIA),
                new IdentityCompositionAssignment(B, 1, A, ProgressiveIdentityStatus.REFERENCIA)),
            "evidence:synthetic",
            "COMPOSITION_PREPARED_V1",
            When);
        return (decision, IdentityCompositionPlanner.Prepare(read, decision));
    }

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
            request,
            planJson,
            reservationJson,
            "synthetic:test",
            Guid.Parse("00000000-0000-0000-0000-000000000301"),
            "PREPARADA");
    }

    [Test]
    public void Exact_prepared_content_is_accepted()
    {
        var (decision, plan) = Build();
        Assert.DoesNotThrow(() => IdentityCompositionLedgerStore.ValidatePreparedContent(
            Receipt(decision, plan, new[] { R }), decision, plan, new[] { R }));
    }

    [Test]
    public void Changed_decision_plan_or_reservations_fail_closed()
    {
        var (decision, plan) = Build();
        var receipt = Receipt(decision, plan, new[] { R });
        var changedDecision = decision with { PolicyVersion = "COMPOSITION_PREPARED_V2" };
        var changedPlan = plan with { RequestHash = new string('0', 64) };

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => IdentityCompositionLedgerStore.ValidatePreparedContent(
                receipt, changedDecision, plan, new[] { R }));
            Assert.Throws<InvalidOperationException>(() => IdentityCompositionLedgerStore.ValidatePreparedContent(
                receipt, decision, changedPlan, new[] { R }));
            Assert.Throws<InvalidOperationException>(() => IdentityCompositionLedgerStore.ValidatePreparedContent(
                receipt, decision, plan, Array.Empty<Guid>()));
        });
    }

    [Test]
    public void Non_prepared_or_malformed_receipt_is_rejected()
    {
        var (decision, plan) = Build();
        var receipt = Receipt(decision, plan, new[] { R });

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => IdentityCompositionLedgerStore.ValidatePreparedContent(
                receipt with { State = "APLICADA" }, decision, plan, new[] { R }));
            Assert.Throws<InvalidOperationException>(() => IdentityCompositionLedgerStore.ValidatePreparedContent(
                receipt with { RequestHash = "ABC" }, decision, plan, new[] { R }));
            Assert.Throws<InvalidOperationException>(() => IdentityCompositionLedgerStore.ValidatePreparedContent(
                receipt with { RequesterReference = " " }, decision, plan, new[] { R }));
        });
    }
}
