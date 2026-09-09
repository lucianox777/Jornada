using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class IdentityCompositionAppliedHistoryTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-000000000201");
    private static readonly DateTimeOffset When = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
    private static IdentityCompositionAppliedHistoryRow Row(Guid[]? members = null)
    {
        var json = IdentityCompositionCanonical.SerializeHistoryMembers(members ?? new[] { A, B });
        return new(D, A, json, IdentityCompositionCanonical.HashUtf8(json), When, true);
    }

    [Test]
    public void ValidHistoryPreservesMembersAndProvenance()
    {
        var history = IdentityCompositionAppliedHistory.Validate(Row());
        Assert.That(history.CompositionId, Is.EqualTo(D));
        Assert.That(history.ReferenceUuid, Is.EqualTo(A));
        Assert.That(history.MemberInitialUuids, Is.EquivalentTo(new[] { A, B }));
    }

    [Test]
    public void MissingAppliedReceiptIsRejected() =>
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionAppliedHistory.Validate(Row() with { HasAppliedReceipt = false }));

    [Test]
    public void HashMismatchIsRejected() =>
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionAppliedHistory.Validate(Row() with { MembersHash = new string('0', 64) }));

    [Test]
    public void NonCanonicalPayloadIsRejected()
    {
        var row = Row() with { MembersJson = "[\"" + B + "\",\"" + A + "\"]" };
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionAppliedHistory.Validate(row));
    }

    [Test]
    public void DuplicateMembersAreRejected()
    {
        var json = "[\"" + A + "\",\"" + A + "\"]";
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionAppliedHistory.Validate(Row() with { MembersJson = json, MembersHash = IdentityCompositionCanonical.HashUtf8(json) }));
    }

    [Test]
    public void DuplicateRowsAreRejected() =>
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionAppliedHistory.ValidateBatch(new[] { Row(), Row() }));

    [Test]
    public void EmptyBatchIsValid() =>
        Assert.That(IdentityCompositionAppliedHistory.ValidateBatch(Array.Empty<IdentityCompositionAppliedHistoryRow>()), Is.Empty);

    [Test]
    public void InvalidJsonIsRejected() =>
        Assert.Throws<InvalidOperationException>(() => IdentityCompositionAppliedHistory.Validate(Row() with { MembersJson = "not-json" }));
}
