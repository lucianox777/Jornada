using System.Collections.Immutable;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class IdentityHistoricalResolverTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static IdentityCompositionHistory History => new(A, D, ImmutableArray.Create(A, B));
    private static IdentityCompositionMember Member(Guid id, Guid? target) =>
        new(id, target, target.HasValue ? ProgressiveIdentityStatus.REFERENCIA : ProgressiveIdentityStatus.INDEFINIDA, 2, null);

    [Test]
    public void Fusion_has_one_successor()
    {
        var result = IdentityHistoricalResolver.Resolve(History, ImmutableArray.Create(Member(A, C), Member(B, C)));
        Assert.That(result.State, Is.EqualTo(HistoricalReferenceState.UNIVOCA));
        Assert.That(result.CanonicalUuid, Is.EqualTo(C));
        Assert.That(result.Candidates, Is.EqualTo(new[] { C }));
    }

    [Test]
    public void Split_never_selects_arbitrary_successor()
    {
        var result = IdentityHistoricalResolver.Resolve(History, ImmutableArray.Create(Member(A, A), Member(B, B)));
        Assert.That(result.State, Is.EqualTo(HistoricalReferenceState.AMBIGUA));
        Assert.That(result.CanonicalUuid, Is.Null);
        Assert.That(result.Candidates, Is.EqualTo(new[] { A, B }));
    }

    [Test]
    public void Unresolved_member_prevents_univocal_resolution()
    {
        var result = IdentityHistoricalResolver.Resolve(History, ImmutableArray.Create(Member(A, C), Member(B, null)));
        Assert.That(result.State, Is.EqualTo(HistoricalReferenceState.INDEFINIDA));
        Assert.That(result.CanonicalUuid, Is.Null);
        Assert.That(result.UnresolvedInitialUuids, Is.EqualTo(new[] { B }));
    }

    [Test]
    public void Missing_member_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => IdentityHistoricalResolver.Resolve(History, ImmutableArray.Create(Member(A, C))));
    }

    [Test]
    public void Duplicate_history_member_is_rejected()
    {
        var history = History with { MemberInitialUuids = ImmutableArray.Create(A, A) };
        Assert.Throws<InvalidOperationException>(() => IdentityHistoricalResolver.Resolve(history, ImmutableArray.Create(Member(A, C))));
    }

    [Test]
    public void Duplicate_current_member_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => IdentityHistoricalResolver.Resolve(History, ImmutableArray.Create(Member(A, C), Member(A, C), Member(B, C))));
    }

    [Test]
    public void Invalid_current_version_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => IdentityHistoricalResolver.Resolve(History, ImmutableArray.Create(Member(A, C) with { Version = 0 }, Member(B, C))));
    }

    [Test]
    public void Invalid_cpf_anchor_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => IdentityHistoricalResolver.Resolve(History, ImmutableArray.Create(Member(A, C) with { CpfAnchorUuid = Guid.Empty }, Member(B, C))));
    }
}
