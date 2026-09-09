using System.Collections.Immutable;
using Jornada.Contracts;
using Jornada.Operational.Sql;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class IdentityCompositionApplicationTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid DecisionId = Guid.Parse("00000000-0000-0000-0000-000000000201");
    private static readonly DateTimeOffset When = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

    private static (IdentityCompositionDecision Decision, IdentityCompositionPlan Plan, IdentityCompositionPreparedReceipt Prepared) Build()
    {
        var read = new IdentityCompositionReadSet(
            ImmutableArray.Create(
                new IdentityCompositionMember(A, A, ProgressiveIdentityStatus.REFERENCIA, 1, null),
                new IdentityCompositionMember(B, B, ProgressiveIdentityStatus.REFERENCIA, 1, null)),
            ImmutableArray<Guid>.Empty,
            ImmutableArray<IdentityCompositionHistory>.Empty);
        var decision = new IdentityCompositionDecision(
            DecisionId,
            IdentityCompositionOperation.FUSAO,
            ImmutableArray.Create(
                new IdentityCompositionAssignment(A, 1, A, ProgressiveIdentityStatus.REFERENCIA),
                new IdentityCompositionAssignment(B, 1, A, ProgressiveIdentityStatus.REFERENCIA)),
            "evidence:application-test",
            "COMPOSITION_APPLICATION_V1",
            When);
        var plan = IdentityCompositionPlanner.Prepare(read, decision);
        var requestJson = IdentityCompositionCanonical.SerializeDecision(decision);
        var planJson = IdentityCompositionCanonical.SerializePlan(plan);
        var reservationsJson = IdentityCompositionCanonical.SerializeReservations(Array.Empty<Guid>());
        var prepared = new IdentityCompositionPreparedReceipt(
            DecisionId,
            IdentityCompositionCanonical.HashUtf8(requestJson),
            IdentityCompositionCanonical.HashUtf8(planJson),
            IdentityCompositionCanonical.HashUtf8(reservationsJson),
            requestJson,
            planJson,
            reservationsJson,
            "synthetic:preparer",
            null,
            "PREPARADA");
        return (decision, plan, prepared);
    }

    private static IdentityCompositionAppliedReceipt Applied(
        IdentityCompositionPreparedReceipt prepared,
        IdentityCompositionPlan plan) =>
        new(
            prepared.DecisionId,
            prepared.RequestHash,
            prepared.PlanHash,
            prepared.ReservationsHash,
            "synthetic:applier",
            When.AddMinutes(1),
            plan.Changes.Length,
            plan.HistoryToAppend.Length,
            "APLICADA");

    [Test]
    public void Canonical_history_members_are_sorted_and_validated()
    {
        var first = IdentityCompositionCanonical.SerializeHistoryMembers(new[] { B, A });
        var second = IdentityCompositionCanonical.SerializeHistoryMembers(new[] { A, B });
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.Throws<InvalidOperationException>(() => IdentityCompositionCanonical.SerializeHistoryMembers(Array.Empty<Guid>()));
            Assert.Throws<InvalidOperationException>(() => IdentityCompositionCanonical.SerializeHistoryMembers(new[] { A, A }));
            Assert.Throws<InvalidOperationException>(() => IdentityCompositionCanonical.SerializeHistoryMembers(new[] { Guid.Empty }));
        });
    }

    [Test]
    public void Exact_applied_receipt_is_accepted()
    {
        var (_, plan, prepared) = Build();
        Assert.DoesNotThrow(() => IdentityCompositionApplicationStore.ValidateAppliedContent(
            Applied(prepared, plan), prepared, plan));
    }

    [Test]
    public void Applied_receipt_hash_or_counts_drift_fails_closed()
    {
        var (_, plan, prepared) = Build();
        var applied = Applied(prepared, plan);
        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => IdentityCompositionApplicationStore.ValidateAppliedContent(
                applied with { PlanHash = new string('0', 64) }, prepared, plan));
            Assert.Throws<InvalidOperationException>(() => IdentityCompositionApplicationStore.ValidateAppliedContent(
                applied with { AppliedChanges = applied.AppliedChanges + 1 }, prepared, plan));
            Assert.Throws<InvalidOperationException>(() => IdentityCompositionApplicationStore.ValidateAppliedContent(
                applied with { RegisteredHistories = applied.RegisteredHistories + 1 }, prepared, plan));
        });
    }

    [Test]
    public void Malformed_or_non_applied_receipt_is_rejected()
    {
        var (_, plan, prepared) = Build();
        var applied = Applied(prepared, plan);
        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => IdentityCompositionApplicationStore.ValidateAppliedContent(
                applied with { State = "PREPARADA" }, prepared, plan));
            Assert.Throws<ArgumentException>(() => IdentityCompositionApplicationStore.ValidateAppliedContent(
                applied with { ApplierReference = " " }, prepared, plan));
            Assert.Throws<InvalidOperationException>(() => IdentityCompositionApplicationStore.ValidateAppliedContent(
                applied with { AppliedAt = When.ToOffset(TimeSpan.FromHours(-3)) }, prepared, plan));
        });
    }
}
