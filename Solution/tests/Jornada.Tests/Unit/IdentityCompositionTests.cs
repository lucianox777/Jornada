using System.Collections.Immutable;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class IdentityCompositionTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid E = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly DateTimeOffset When = new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);

    private static IdentityCompositionMember Member(Guid id, Guid? target, long version = 1, Guid? anchor = null) =>
        new(id, target, target is null ? ProgressiveIdentityStatus.INDEFINIDA : ProgressiveIdentityStatus.REFERENCIA, version, anchor);

    private static IdentityCompositionReadSet Set(params IdentityCompositionMember[] members) =>
        new(members.ToImmutableArray(), ImmutableArray<Guid>.Empty, ImmutableArray<IdentityCompositionHistory>.Empty);

    private static IdentityCompositionDecision Decision(IdentityCompositionOperation kind,
        IdentityCompositionReadSet read, params (Guid Id, Guid? Target)[] changes) =>
        new(Guid.NewGuid(), kind, changes.Select(c =>
        {
            var member = read.Members.Single(m => m.InitialUuid == c.Id);
            return new IdentityCompositionAssignment(c.Id, member.Version, c.Target,
                c.Target is null ? ProgressiveIdentityStatus.INDEFINIDA : ProgressiveIdentityStatus.REFERENCIA);
        }).ToImmutableArray(), "evidence:synthetic", "COMPOSITION_TEST_V1", When);

    private static IdentityCompositionReadSet Apply(IdentityCompositionReadSet read, IdentityCompositionPlan plan)
    {
        var changes = plan.Changes.ToDictionary(c => c.InitialUuid);
        return read with
        {
            Members = read.Members.Select(m => changes.TryGetValue(m.InitialUuid, out var c)
                ? m with { CanonicalUuid = c.AfterUuid, Status = c.AfterStatus, Version = c.NewVersion } : m).ToImmutableArray(),
            ReservedNewUuids = read.ReservedNewUuids.Except(plan.Changes.Where(c => c.AfterUuid is not null)
                .Select(c => c.AfterUuid!.Value)).ToImmutableArray(),
            History = read.History.AddRange(plan.HistoryToAppend)
        };
    }

    [Test]
    public void Merge_then_split_preserves_initial_ids_and_historical_ambiguity()
    {
        var original = Set(Member(A, A), Member(B, B));
        var merge = IdentityCompositionPlanner.Prepare(original,
            Decision(IdentityCompositionOperation.FUSAO, original, (A, A), (B, A)));
        var merged = Apply(original, merge);
        Assert.Multiple(() =>
        {
            Assert.That(merged.Members.Select(m => m.InitialUuid), Is.EquivalentTo(new[] { A, B }));
            Assert.That(merged.Members.Select(m => m.CanonicalUuid), Is.All.EqualTo(A));
            Assert.That(merge.Changes, Has.Length.EqualTo(1));
            Assert.That(merge.HistoryToAppend, Has.Length.EqualTo(2));
        });
        var splitRead = merged with { ReservedNewUuids = ImmutableArray.Create(C, D) };
        var split = IdentityCompositionPlanner.Prepare(splitRead,
            Decision(IdentityCompositionOperation.SEPARACAO, splitRead, (A, C), (B, D)));
        var separated = Apply(splitRead, split);
        var historical = IdentityCompositionPlanner.ResolveHistorical(split.HistoryToAppend.Single(), separated.Members);
        Assert.Multiple(() =>
        {
            Assert.That(historical.State, Is.EqualTo(HistoricalReferenceState.AMBIGUA));
            Assert.That(historical.CanonicalUuid, Is.Null);
            Assert.That(historical.Candidates, Is.EquivalentTo(new[] { C, D }));
            Assert.That(IdentityCompositionPlanner.ResolveHistorical(
                merge.HistoryToAppend.Single(h => h.ReferenceUuid == B), separated.Members).CanonicalUuid,
                Is.EqualTo(D));
            Assert.That(separated.Members.Single(m => m.InitialUuid == B).Version, Is.EqualTo(3));
            Assert.That(original.Members.Single(m => m.InitialUuid == B).CanonicalUuid, Is.EqualTo(B));
        });
    }

    [Test]
    public void Historical_membership_survives_later_reassociation_without_chasing_aliases()
    {
        var read = Set(Member(A, A), Member(B, A), Member(C, C)) with
        {
            ReservedNewUuids = ImmutableArray.Create(D, E)
        };
        var split = IdentityCompositionPlanner.Prepare(read,
            Decision(IdentityCompositionOperation.SEPARACAO, read, (A, D), (B, E)));
        var after = Apply(read, split);
        var regroup = IdentityCompositionPlanner.Prepare(after,
            Decision(IdentityCompositionOperation.REASSOCIACAO, after, (B, C), (C, C)));
        after = Apply(after, regroup);
        var resolution = IdentityCompositionPlanner.ResolveHistorical(split.HistoryToAppend.Single(), after.Members);
        Assert.That(resolution.State, Is.EqualTo(HistoricalReferenceState.AMBIGUA));
        Assert.That(resolution.Candidates, Is.EquivalentTo(new[] { D, C }));
    }

    [Test]
    public void Anchored_identity_can_absorb_unanchored_origin_but_cannot_move()
    {
        var read = Set(Member(A, A, anchor: A), Member(B, B));
        var plan = IdentityCompositionPlanner.Prepare(read,
            Decision(IdentityCompositionOperation.FUSAO, read, (A, A), (B, A)));
        Assert.That(plan.Changes.Single().AfterUuid, Is.EqualTo(A));
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPlanner.Prepare(read,
            Decision(IdentityCompositionOperation.FUSAO, read, (A, B), (B, B))));
    }

    [Test]
    public void Distinct_cpf_anchors_cannot_be_merged_even_with_explicit_destination()
    {
        var read = Set(Member(A, A, anchor: A), Member(B, B, anchor: B));
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPlanner.Prepare(read,
            Decision(IdentityCompositionOperation.FUSAO, read, (A, A), (B, A))));
    }

    [Test]
    public void Split_can_leave_uncertain_origin_without_inventing_a_destination()
    {
        var read = Set(Member(A, A), Member(B, A));
        var split = IdentityCompositionPlanner.Prepare(read,
            Decision(IdentityCompositionOperation.SEPARACAO, read, (A, A), (B, null)));
        var resolution = IdentityCompositionPlanner.ResolveHistorical(split.HistoryToAppend.Single(), Apply(read, split).Members);
        Assert.That(resolution.State, Is.EqualTo(HistoricalReferenceState.INDEFINIDA));
        Assert.That(resolution.CanonicalUuid, Is.Null);
        Assert.That(resolution.Candidates, Is.EquivalentTo(new[] { A }));
    }

    [Test]
    public void Incomplete_partition_and_stale_versions_fail_closed()
    {
        var read = Set(Member(A, A), Member(B, A), Member(C, C));
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPlanner.Prepare(read,
            Decision(IdentityCompositionOperation.REASSOCIACAO, read, (B, C), (C, C))));
        var decision = Decision(IdentityCompositionOperation.SEPARACAO, read, (A, A), (B, B));
        var stale = decision with { Assignments = decision.Assignments.SetItem(0, decision.Assignments[0] with { ExpectedVersion = 0 }) };
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPlanner.Prepare(read, stale));
    }

    [Test]
    public void Reusing_a_historical_alias_or_someone_elses_initial_uuid_is_rejected()
    {
        var history = new IdentityCompositionHistory(D, Guid.NewGuid(), ImmutableArray.Create(A));
        var read = Set(Member(A, A), Member(B, B)) with { History = ImmutableArray.Create(history) };
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPlanner.Prepare(read,
            Decision(IdentityCompositionOperation.REASSOCIACAO, read, (A, D))));
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPlanner.Prepare(read,
            Decision(IdentityCompositionOperation.REASSOCIACAO, read, (A, B), (B, A))));
    }

    [Test]
    public void Fully_decided_unanchored_split_requires_new_or_already_admitted_destinations()
    {
        var read = Set(Member(A, A), Member(B, A));
        var usingOldReference = read with { ReservedNewUuids = ImmutableArray.Create(D) };
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPlanner.Prepare(usingOldReference,
            Decision(IdentityCompositionOperation.SEPARACAO, usingOldReference, (A, A), (B, D))));
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPlanner.Prepare(read,
            Decision(IdentityCompositionOperation.SEPARACAO, read, (A, A), (B, B))));

        var reserved = read with { ReservedNewUuids = ImmutableArray.Create(C, D) };
        var plan = IdentityCompositionPlanner.Prepare(reserved,
            Decision(IdentityCompositionOperation.SEPARACAO, reserved, (A, C), (B, D)));
        Assert.Multiple(() =>
        {
            Assert.That(plan.Changes, Has.Length.EqualTo(2));
            Assert.That(plan.Changes.Single(x => x.InitialUuid == A).AfterUuid, Is.EqualTo(C));
            Assert.That(plan.Changes.Single(x => x.InitialUuid == B).AfterUuid, Is.EqualTo(D));
            Assert.That(plan.Changes.Select(x => x.InitialUuid), Is.EquivalentTo(new[] { A, B }));
        });
    }

    [Test]
    public void Cpf_anchored_split_preserves_anchor_and_allocates_other_destination()
    {
        var read = Set(Member(A, A, anchor: A), Member(B, A)) with
        {
            ReservedNewUuids = ImmutableArray.Create(D)
        };
        var plan = IdentityCompositionPlanner.Prepare(read,
            Decision(IdentityCompositionOperation.SEPARACAO, read, (A, A), (B, D)));
        Assert.Multiple(() =>
        {
            Assert.That(plan.Changes.Single(x => x.InitialUuid == B).AfterUuid, Is.EqualTo(D));
            Assert.That(plan.Changes.Any(x => x.InitialUuid == A && x.AfterUuid != A), Is.False);
        });
    }

    [Test]
    public void Decision_hash_is_order_independent_and_history_is_not_the_replay_ledger()
    {
        var read = Set(Member(A, A), Member(B, B));
        var decision = Decision(IdentityCompositionOperation.FUSAO, read, (A, A), (B, A));
        var first = IdentityCompositionPlanner.Prepare(read, decision);
        var reverse = IdentityCompositionPlanner.Prepare(read, decision with { Assignments = decision.Assignments.Reverse().ToImmutableArray() });
        Assert.That(reverse.RequestHash, Is.EqualTo(first.RequestHash));

        var withHistoryOnly = read with { History = first.HistoryToAppend };
        var replanned = IdentityCompositionPlanner.Prepare(withHistoryOnly, decision);
        Assert.That(replanned.RequestHash, Is.EqualTo(first.RequestHash));
        Assert.That(IdentityCompositionPlanner.Prepare(read, decision with { DecisionId = Guid.NewGuid() }).RequestHash,
            Is.Not.EqualTo(first.RequestHash));
    }

    [Test]
    public void Duplicate_unknown_or_invalid_assignments_are_rejected()
    {
        var read = Set(Member(A, A), Member(B, B));
        var decision = Decision(IdentityCompositionOperation.FUSAO, read, (A, A), (B, A));
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPlanner.Prepare(read,
            decision with { Assignments = decision.Assignments.Add(decision.Assignments[0]) }));
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPlanner.Prepare(read,
            decision with { Assignments = decision.Assignments.SetItem(1, decision.Assignments[1] with { InitialUuid = C }) }));
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPlanner.Prepare(read,
            decision with { Assignments = decision.Assignments.SetItem(1, decision.Assignments[1] with { TargetUuid = Guid.Empty }) }));
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPlanner.Prepare(read,
            decision with { Assignments = decision.Assignments.SetItem(1, decision.Assignments[1] with { Status = (ProgressiveIdentityStatus)999 }) }));
        Assert.Throws<ArgumentException>(() => IdentityCompositionPlanner.Prepare(read,
            decision with { DecidedAt = When.ToOffset(TimeSpan.FromHours(-3)) }));
    }

    [Test]
    public void Missing_historical_members_never_produce_a_unique_answer()
    {
        var history = new IdentityCompositionHistory(A, Guid.NewGuid(), ImmutableArray.Create(A, B));
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPlanner.ResolveHistorical(history,
            ImmutableArray.Create(Member(A, A))));
    }

    [Test]
    public void Historical_resolution_rejects_duplicate_or_invalid_current_snapshots()
    {
        var history = new IdentityCompositionHistory(A, Guid.NewGuid(), ImmutableArray.Create(A));
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPlanner.ResolveHistorical(history,
            ImmutableArray.Create(Member(A, A), Member(A, A))));
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPlanner.ResolveHistorical(history,
            ImmutableArray.Create(new IdentityCompositionMember(A, null, ProgressiveIdentityStatus.REFERENCIA, 1, null))));
    }

    [Test]
    public void No_op_is_not_a_new_composition_decision()
    {
        var read = Set(Member(A, A), Member(B, B));
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionPlanner.Prepare(read,
            Decision(IdentityCompositionOperation.REASSOCIACAO, read, (A, A))));
    }
}
