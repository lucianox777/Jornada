using Jornada.Linkage.Parameters.Worker;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9IdentityClusterFdrTests
{
    [Test]
    public void Aggregates_by_base_identity_and_never_certifies()
    {
        var result = V9IdentityClusterFdr.Describe([
            new("base-A", 9, 1),
            new("base-B", 90, 0),
            new("base-C", 0, 0)
        ]);
        Assert.That(result.IndependentGroups, Is.EqualTo(3));
        Assert.That(result.ObservedFdr, Is.EqualTo(0.01).Within(1e-12));
        Assert.That(result.MaximumLeaveOneGroupOutFdr, Is.EqualTo(0.1).Within(1e-12));
        Assert.That(result.Certified, Is.False);
    }

    [Test]
    public void Zero_accepted_is_undefined_not_zero_risk()
    {
        var result = V9IdentityClusterFdr.Describe([
            new("base-A", 0, 0), new("base-B", 0, 0)
        ]);
        Assert.That(result.ObservedFdr, Is.Null);
        Assert.That(result.MaximumLeaveOneGroupOutFdr, Is.Null);
        Assert.That(result.Certified, Is.False);
    }

    [Test]
    public void Rejects_duplicate_or_invalid_base_groups()
    {
        Assert.That((Action)(() => V9IdentityClusterFdr.Describe([
            new("base-A", 1, 0), new("base-A", 1, 0)
        ])), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9IdentityClusterFdr.Describe([
            new("base-A", -1, 0), new("base-B", 1, 0)
        ])), Throws.TypeOf<InvalidDataException>());
    }
}
