using System.Collections.Immutable;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt17IdentityCompositionRegressionTests
{
    private static readonly Guid A = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid I2 = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid B = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid C = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset When = new(2026, 9, 29, 23, 0, 0, TimeSpan.Zero);

    [Test]
    public void DcId02_unanchored_decided_split_requires_two_new_canonical_uuids_and_makes_old_reference_historical()
    {
        var read = Read(Member(A, A), Member(I2, A)) with { ReservedNewUuids = [B, C] };
        var plan = IdentityCompositionPlanner.Prepare(read, Decision(read, (A, B), (I2, C)));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(plan.Changes.Select(x => x.AfterUuid), Is.EquivalentTo(new Guid?[] { B, C }));
            Assert.That(plan.Changes.Any(x => x.AfterUuid == A), Is.False);
            Assert.That(plan.HistoryToAppend.Single().ReferenceUuid, Is.EqualTo(A));
            Assert.That(plan.HistoryToAppend.Single().MemberInitialUuids, Is.EquivalentTo(new[] { A, I2 }));
        }));
    }

    [Test]
    public void DcId02_cpf_anchor_keeps_holder_on_old_uuid_and_moves_only_other_person()
    {
        var read = Read(Member(A, A, A), Member(I2, A)) with { ReservedNewUuids = [B] };
        var plan = IdentityCompositionPlanner.Prepare(read, Decision(read, (A, A), (I2, B)));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(plan.Changes.Any(x => x.InitialUuid == A && x.AfterUuid != A), Is.False);
            Assert.That(plan.Changes.Single(x => x.InitialUuid == I2).AfterUuid, Is.EqualTo(B));
            Assert.That(plan.HistoryToAppend.Single().ReferenceUuid, Is.EqualTo(A));
        }));
    }

    [Test]
    public void DcId02_bare_initial_uuid_is_not_promoted_to_canonical_destination()
    {
        var read = Read(Member(A, A), Member(I2, A)) with { ReservedNewUuids = [B] };
        Assert.Throws<InvalidOperationException>((Action)(() =>
            IdentityCompositionPlanner.Prepare(read, Decision(read, (A, B), (I2, I2)))));
    }

    private static IdentityCompositionMember Member(Guid initial, Guid canonical, Guid? anchor = null) =>
        new(initial, canonical, ProgressiveIdentityStatus.REFERENCIA, 1, anchor);

    private static IdentityCompositionReadSet Read(params IdentityCompositionMember[] members) =>
        new(members.ToImmutableArray(), ImmutableArray<Guid>.Empty, ImmutableArray<IdentityCompositionHistory>.Empty);

    private static IdentityCompositionDecision Decision(IdentityCompositionReadSet read, params (Guid Initial, Guid? Target)[] changes) =>
        new(Guid.Parse("30000000-0000-0000-0000-000000000001"), IdentityCompositionOperation.SEPARACAO,
            changes.Select(x => new IdentityCompositionAssignment(
                x.Initial, read.Members.Single(m => m.InitialUuid == x.Initial).Version, x.Target,
                x.Target is null ? ProgressiveIdentityStatus.INDEFINIDA : ProgressiveIdentityStatus.REFERENCIA)).ToImmutableArray(),
            "DT17:DC-ID-02", "DC-ID-02-20260929", When);
}
