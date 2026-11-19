using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class V9OperationalIdentityPartitionParityTests
{
    [Test]
    public void Same_membership_with_different_identity_guids_is_equal()
    {
        var left = new Dictionary<int, Guid>
        {
            [1] = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            [2] = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            [3] = Guid.Parse("00000000-0000-0000-0000-000000000002")
        };
        var right = new Dictionary<int, Guid>
        {
            [3] = Guid.NewGuid(),
            [2] = Guid.Empty,
            [1] = Guid.Empty
        };
        Assert.That(V9OperationalCandidateGraphParityAudit.CanonicalGroups(left),
            Is.EqualTo(V9OperationalCandidateGraphParityAudit.CanonicalGroups(right)));
    }

    [Test]
    public void Split_or_merge_is_detected()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var baseline = new Dictionary<int, Guid> { [1] = a, [2] = a, [3] = b };
        var split = new Dictionary<int, Guid> { [1] = a, [2] = b, [3] = b };
        var merged = new Dictionary<int, Guid> { [1] = a, [2] = a, [3] = a };
        Assert.That(V9OperationalCandidateGraphParityAudit.CanonicalGroups(split),
            Is.Not.EqualTo(V9OperationalCandidateGraphParityAudit.CanonicalGroups(baseline)));
        Assert.That(V9OperationalCandidateGraphParityAudit.CanonicalGroups(merged),
            Is.Not.EqualTo(V9OperationalCandidateGraphParityAudit.CanonicalGroups(baseline)));
    }

    [Test]
    public void Membership_order_does_not_change_partition()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var first = new Dictionary<int, Guid> { [10] = a, [2] = a, [7] = b };
        var reordered = new Dictionary<int, Guid> { [7] = b, [2] = a, [10] = a };
        Assert.That(V9OperationalCandidateGraphParityAudit.CanonicalGroups(first),
            Is.EqualTo(V9OperationalCandidateGraphParityAudit.CanonicalGroups(reordered)));
    }
}
