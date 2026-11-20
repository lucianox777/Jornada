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
    public void Guid_sensitive_receipt_digest_can_differ_for_equivalent_partitions()
    {
        // Characterization only: do not change the existing receipt contract.
        var first = new Dictionary<int, Guid>
        {
            [1] = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            [2] = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            [3] = Guid.Parse("00000000-0000-0000-0000-000000000002")
        };
        var relabeled = new Dictionary<int, Guid>
        {
            [1] = Guid.Parse("00000000-0000-0000-0000-000000000003"),
            [2] = Guid.Parse("00000000-0000-0000-0000-000000000003"),
            [3] = Guid.Parse("00000000-0000-0000-0000-000000000004")
        };

        static string CurrentReceiptDigest(IReadOnlyDictionary<int, Guid> groups)
        {
            var serialized = string.Join("\\n", groups.OrderBy(x => x.Key)
                .Select(x => FormattableString.Invariant($"{x.Key}:{x.Value:D}")));
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(serialized))).ToLowerInvariant();
        }

        Assert.That(V9OperationalCandidateGraphParityAudit.CanonicalGroups(first),
            Is.EqualTo(V9OperationalCandidateGraphParityAudit.CanonicalGroups(relabeled)));
        Assert.That(CurrentReceiptDigest(first), Is.Not.EqualTo(CurrentReceiptDigest(relabeled)));
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
    public void Same_group_sizes_with_different_memberships_are_not_equivalent()
    {
        // Counts alone cannot detect a cross-batch identity swap.
        var first = new Dictionary<int, Guid>
        {
            [1] = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            [2] = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            [3] = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            [4] = Guid.Parse("00000000-0000-0000-0000-000000000002")
        };
        var swapped = new Dictionary<int, Guid>
        {
            [1] = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            [3] = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            [2] = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            [4] = Guid.Parse("00000000-0000-0000-0000-000000000002")
        };
        Assert.That(first.Values.Distinct().Count(), Is.EqualTo(swapped.Values.Distinct().Count()));
        Assert.That(first.Values.GroupBy(id => id).Select(g => g.Count()).OrderBy(n => n),
            Is.EqualTo(swapped.Values.GroupBy(id => id).Select(g => g.Count()).OrderBy(n => n)));
        Assert.That(V9OperationalCandidateGraphParityAudit.CanonicalGroups(first),
            Is.Not.EqualTo(V9OperationalCandidateGraphParityAudit.CanonicalGroups(swapped)));
    }

    [Test]
    public void Missing_observation_is_not_masked_by_identical_remaining_groups()
    {
        var shared = Guid.NewGuid();
        var isolated = Guid.NewGuid();
        var complete = new Dictionary<int, Guid>
        {
            [1] = shared, [2] = shared, [3] = isolated
        };
        var dropped = new Dictionary<int, Guid>
        {
            [1] = shared, [2] = shared
        };
        Assert.That(V9OperationalCandidateGraphParityAudit.CanonicalGroups(complete),
            Is.Not.EqualTo(V9OperationalCandidateGraphParityAudit.CanonicalGroups(dropped)));
    }

    [Test]
    public void Singleton_identity_must_not_be_absorbed_into_another_group()
    {
        var shared = Guid.NewGuid();
        var isolated = Guid.NewGuid();
        var expected = new Dictionary<int, Guid>
        {
            [1] = shared, [2] = shared, [3] = isolated
        };
        var absorbed = new Dictionary<int, Guid>
        {
            [1] = shared, [2] = shared, [3] = shared
        };
        Assert.That(V9OperationalCandidateGraphParityAudit.CanonicalGroups(expected),
            Is.Not.EqualTo(V9OperationalCandidateGraphParityAudit.CanonicalGroups(absorbed)));
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
