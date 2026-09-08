using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ProgressiveIdentityTests
{
    private static readonly Guid Initial = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    private static readonly Guid Existing = Guid.Parse("a1000000-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset Created = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void InitialUuidIsAllocatedBeforeResolutionWithoutInventingAnAssociation()
    {
        var state = ProgressiveIdentityLifecycle.Create(Initial, Created);
        Assert.Multiple(() =>
        {
            Assert.That(state.InitialUuid, Is.EqualTo(Initial));
            Assert.That(state.CanonicalUuid, Is.Null);
            Assert.That(state.Status, Is.EqualTo(ProgressiveIdentityStatus.PROVISORIA));
            Assert.That(state.Version, Is.Zero);
            Assert.That(state.LastResolutionAt, Is.Null);
        });
    }

    [Test]
    public void CompletedSearchWithoutCandidatesEstablishesTheInitialReference()
    {
        var state = ProgressiveIdentityLifecycle.Create(Initial, Created);
        var result = ProgressiveIdentityLifecycle.Conclude(state, Decision(state, ProgressiveResolutionOutcome.NOVA_IDENTIDADE));
        Assert.That(result.Status, Is.EqualTo(ProgressiveIdentityStatus.REFERENCIA));
        Assert.That(result.CanonicalUuid, Is.EqualTo(Initial));
        Assert.That(result.Version, Is.EqualTo(1));
        Assert.That(result.InitialUuid, Is.EqualTo(Initial));
    }

    [Test]
    public void ExistingIdentityIsSelectedOnlyByAnExplicitCompletedDecision()
    {
        var state = ProgressiveIdentityLifecycle.Create(Initial, Created);
        var decision = Decision(state, ProgressiveResolutionOutcome.ASSOCIACAO_EXISTENTE, Existing);
        var result = ProgressiveIdentityLifecycle.Conclude(state, decision);
        Assert.That(result.CanonicalUuid, Is.EqualTo(Existing));
        Assert.That(result.Status, Is.EqualTo(ProgressiveIdentityStatus.REFERENCIA));
        Assert.That(result.InitialUuid, Is.EqualTo(Initial));
        Assert.That(ProgressiveIdentityLifecycle.Conclude(result, decision), Is.SameAs(result));
    }

    [Test]
    public void AmbiguityKeepsTheReferenceWithoutChoosingAnyCandidate()
    {
        var state = ProgressiveIdentityLifecycle.Create(Initial, Created);
        var result = ProgressiveIdentityLifecycle.Conclude(state, Decision(state, ProgressiveResolutionOutcome.INDEFINIDA));
        Assert.That(result.Status, Is.EqualTo(ProgressiveIdentityStatus.INDEFINIDA));
        Assert.That(result.CanonicalUuid, Is.Null);
        Assert.That(result.InitialUuid, Is.EqualTo(Initial));
        var later = ProgressiveIdentityLifecycle.Conclude(result,
            Decision(result, ProgressiveResolutionOutcome.ASSOCIACAO_EXISTENTE, Existing));
        Assert.That(later.Status, Is.EqualTo(ProgressiveIdentityStatus.REFERENCIA));
        Assert.That(later.CanonicalUuid, Is.EqualTo(Existing));
        Assert.That(later.Version, Is.EqualTo(2));
    }

    [Test]
    public void ReevaluationDoesNotCreateAnotherProvisionalState()
    {
        var initial = ProgressiveIdentityLifecycle.Create(Initial, Created);
        var first = ProgressiveIdentityLifecycle.Conclude(initial, Decision(initial, ProgressiveResolutionOutcome.NOVA_IDENTIDADE));
        var second = ProgressiveIdentityLifecycle.Conclude(first, Decision(first, ProgressiveResolutionOutcome.INDEFINIDA));
        Assert.That(second.Status, Is.EqualTo(ProgressiveIdentityStatus.INDEFINIDA));
        Assert.That(second.Version, Is.EqualTo(2));
        Assert.That(second.InitialUuid, Is.EqualTo(Initial));
    }

    [Test]
    public void UncertaintySuspendsAnEarlierBindingAndNeverSilentlySeparatesIt()
    {
        var initial = ProgressiveIdentityLifecycle.Create(Initial, Created);
        var associated = ProgressiveIdentityLifecycle.Conclude(initial,
            Decision(initial, ProgressiveResolutionOutcome.ASSOCIACAO_EXISTENTE, Existing));
        var uncertain = ProgressiveIdentityLifecycle.Conclude(associated,
            Decision(associated, ProgressiveResolutionOutcome.INDEFINIDA));
        Assert.That(uncertain.CanonicalUuid, Is.Null);
        Assert.Throws<InvalidOperationException>(() => ProgressiveIdentityLifecycle.Conclude(associated,
            Decision(associated, ProgressiveResolutionOutcome.NOVA_IDENTIDADE)));
        Assert.Throws<InvalidOperationException>(() => ProgressiveIdentityLifecycle.Conclude(uncertain,
            Decision(uncertain, ProgressiveResolutionOutcome.NOVA_IDENTIDADE)));
        Assert.Throws<InvalidOperationException>(() => ProgressiveIdentityLifecycle.Conclude(uncertain,
            Decision(uncertain, ProgressiveResolutionOutcome.ASSOCIACAO_EXISTENTE, Initial)));
        Assert.That(uncertain.LastExternalAssociationUuid, Is.EqualTo(Existing));
        Assert.That(associated.CanonicalUuid, Is.EqualTo(Existing));
    }

    [Test]
    public void InvalidOrIncompleteDecisionsFailClosed()
    {
        var state = ProgressiveIdentityLifecycle.Create(Initial, Created);
        var valid = Decision(state, ProgressiveResolutionOutcome.NOVA_IDENTIDADE);
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Create(Guid.Empty, Created));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Create(Initial, Created.ToOffset(TimeSpan.FromHours(-3))));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state, valid with { Complete = false }));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state, valid with { InitialUuid = Existing }));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state, valid with { EvidenceReference = " " }));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state, valid with { UniverseReference = null }));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state, valid with { TargetUuid = Existing }));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state, valid with { DecidedAt = Created.AddMinutes(-1) }));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state,
            Decision(state, ProgressiveResolutionOutcome.ASSOCIACAO_EXISTENTE)));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state,
            Decision(state, ProgressiveResolutionOutcome.INDEFINIDA, Existing)));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state,
            valid with { Outcome = (ProgressiveResolutionOutcome)999 }));
    }

    [Test]
    public void OptimisticVersionAndDecisionIdPreventStaleOrConflictingReplay()
    {
        var state = ProgressiveIdentityLifecycle.Create(Initial, Created);
        var decision = Decision(state, ProgressiveResolutionOutcome.NOVA_IDENTIDADE);
        var referenced = ProgressiveIdentityLifecycle.Conclude(state, decision);
        Assert.Throws<InvalidOperationException>(() => ProgressiveIdentityLifecycle.Conclude(referenced,
            decision with { Outcome = ProgressiveResolutionOutcome.INDEFINIDA }));
        Assert.Throws<InvalidOperationException>(() => ProgressiveIdentityLifecycle.Conclude(referenced,
            Decision(state, ProgressiveResolutionOutcome.INDEFINIDA)));
        Assert.Throws<InvalidOperationException>(() => ProgressiveIdentityLifecycle.Conclude(referenced with { Status = ProgressiveIdentityStatus.PROVISORIA },
            Decision(referenced, ProgressiveResolutionOutcome.INDEFINIDA)));
        Assert.Throws<InvalidOperationException>(() => ProgressiveIdentityLifecycle.Conclude(referenced with { Version = -1 },
            Decision(referenced, ProgressiveResolutionOutcome.INDEFINIDA)));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(referenced,
            Decision(referenced, ProgressiveResolutionOutcome.INDEFINIDA) with { DecidedAt = Created.AddMinutes(-1) }));
    }

    [Test]
    public void ReferenceStatusIsNotALegacyResolutionOrAnOutcome()
    {
        Assert.That(Enum.GetNames<ProgressiveIdentityStatus>(), Is.EquivalentTo(new[] { "PROVISORIA", "REFERENCIA", "INDEFINIDA" }));
        Assert.That(Enum.GetNames<ProgressiveResolutionOutcome>(), Is.EquivalentTo(new[] { "NOVA_IDENTIDADE", "ASSOCIACAO_EXISTENTE", "INDEFINIDA" }));
        Assert.That(ProgressiveIdentityLifecycle.Version, Is.EqualTo("PROGRESSIVE_IDENTITY_V2"));
    }

    private static ProgressiveIdentityDecision Decision(ProgressiveIdentitySnapshot state,
        ProgressiveResolutionOutcome outcome, Guid? target = null) =>
        new(Guid.NewGuid(), state.InitialUuid, state.Version, outcome, target, true,
            "evidence:synthetic", "POLICY_TEST_V1", Created.AddMinutes(state.Version + 1),
            "SYNTHETIC_MODEL_V1", "frame:synthetic");
}
