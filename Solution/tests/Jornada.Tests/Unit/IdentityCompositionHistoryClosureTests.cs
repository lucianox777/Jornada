using System.Collections.Immutable;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class IdentityCompositionHistoryClosureTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static IdentityCompositionHistory History(Guid reference, Guid decision, params Guid[] members) =>
        new(reference, decision, members.ToImmutableArray());
    private static IdentityCompositionMember Member(Guid id, Guid? target) =>
        new(id, target, target.HasValue ? ProgressiveIdentityStatus.REFERENCIA : ProgressiveIdentityStatus.INDEFINIDA, 2, null);

    [Test]
    public void EmptyHistoryRequiresNoAdditionalMembers() =>
        Assert.That(IdentityCompositionHistoryClosure.MissingMembers(Array.Empty<IdentityCompositionHistory>(), new[] { A }), Is.Empty);

    [Test]
    public void MissingMembersAreUniqueAndDeterministic()
    {
        var history = new[] { History(A, D, A, B), History(B, C, B, C) };
        Assert.That(IdentityCompositionHistoryClosure.MissingMembers(history, new[] { A }), Is.EqualTo(new[] { B, C }));
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionHistoryClosure.RequireComplete(history, new[] { A, B }));
        Assert.DoesNotThrow(() => IdentityCompositionHistoryClosure.RequireComplete(history, new[] { A, B, C }));
    }

    [Test]
    public void DuplicateHistoryKeysAreRejected() =>
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionHistoryClosure.MissingMembers(
            new[] { History(A, D, A), History(A, D, B) }, new[] { A, B }));

    [Test]
    public void DuplicateAndEmptyMembersAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionHistoryClosure.MissingMembers(new[] { History(A, D, A, A) }, new[] { A }));
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionHistoryClosure.MissingMembers(new[] { History(A, D, Guid.Empty) }, new[] { A }));
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionHistoryClosure.MissingMembers(new[] { History(A, D) }, new[] { A }));
    }

    [Test]
    public void SuccessiveSplitsPreserveAmbiguityWithoutArbitrarySuccessor()
    {
        var history = History(A, D, A, B, C);
        var current = ImmutableArray.Create(Member(A, A), Member(B, B), Member(C, C));
        var result = IdentityHistoricalResolver.Resolve(history, current);
        Assert.That(result.State, Is.EqualTo(HistoricalReferenceState.AMBIGUA));
        Assert.That(result.CanonicalUuid, Is.Null);
        Assert.That(result.Candidates, Is.EquivalentTo(new[] { A, B, C }));
    }

    [Test]
    public void HistoricalReferenceCanReappearAsDestinationWithoutRecycling()
    {
        var result = IdentityHistoricalResolver.Resolve(History(A, D, A, B),
            ImmutableArray.Create(Member(A, A), Member(B, A)));
        Assert.That(result.State, Is.EqualTo(HistoricalReferenceState.UNIVOCA));
        Assert.That(result.CanonicalUuid, Is.EqualTo(A));
    }
}
