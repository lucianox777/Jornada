using System.Collections.Immutable;
using Jornada.Contracts;
using Xunit;

namespace Jornada.Tests.Unit;

public sealed class IdentityHistoricalResolverTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static IdentityCompositionHistory History => new(A, D, ImmutableArray.Create(A, B));
    private static IdentityCompositionMember Member(Guid id, Guid? target) =>
        new(id, target, target.HasValue ? ProgressiveIdentityStatus.REFERENCIA : ProgressiveIdentityStatus.INDEFINIDA, 2, null);

    [Fact]
    public void Fusion_has_one_successor()
    {
        var result = IdentityHistoricalResolver.Resolve(History, ImmutableArray.Create(Member(A, C), Member(B, C)));
        Assert.Equal(HistoricalReferenceState.UNIVOCA, result.State);
        Assert.Equal(C, result.CanonicalUuid);
        Assert.Equal(new[] { C }, result.Candidates);
    }

    [Fact]
    public void Split_never_selects_arbitrary_successor()
    {
        var result = IdentityHistoricalResolver.Resolve(History, ImmutableArray.Create(Member(A, A), Member(B, B)));
        Assert.Equal(HistoricalReferenceState.AMBIGUA, result.State);
        Assert.Null(result.CanonicalUuid);
        Assert.Equal(new[] { A, B }, result.Candidates);
    }

    [Fact]
    public void Unresolved_member_prevents_univocal_resolution()
    {
        var result = IdentityHistoricalResolver.Resolve(History, ImmutableArray.Create(Member(A, C), Member(B, null)));
        Assert.Equal(HistoricalReferenceState.INDEFINIDA, result.State);
        Assert.Null(result.CanonicalUuid);
        Assert.Equal(new[] { B }, result.UnresolvedInitialUuids);
    }

    [Fact]
    public void Missing_member_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => IdentityHistoricalResolver.Resolve(History, ImmutableArray.Create(Member(A, C))));
    }

    [Fact]
    public void Duplicate_history_member_is_rejected()
    {
        var history = History with { MemberInitialUuids = ImmutableArray.Create(A, A) };
        Assert.Throws<InvalidOperationException>(() => IdentityHistoricalResolver.Resolve(history, ImmutableArray.Create(Member(A, C))));
    }
}
