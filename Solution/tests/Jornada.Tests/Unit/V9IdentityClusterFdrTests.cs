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
    [Test]
    public void Hoeffding_cluster_bound_is_conservative_and_exceeds_point_estimate()
    {
        var groups = Enumerable.Range(0, 1000)
            .Select(i => new V9IdentityClusterFdr.IdentityGroup(
                "base-" + i, i < 10 ? 0 : 1, i < 10 ? 1 : 0));
        var result = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatio(groups, 1);
        Assert.That(result.ObservedFdr, Is.EqualTo(0.01).Within(1e-12));
        Assert.That(result.UpperFdr, Is.GreaterThan(result.ObservedFdr!.Value));
        Assert.That(result.UpperFdr, Is.LessThanOrEqualTo(1d));
        Assert.That(result.DenominatorSupported, Is.True);
        Assert.That(result.OperationallyCertified, Is.False);
    }

    [Test]
    public void Hoeffding_cluster_bound_refuses_zero_or_insufficient_denominator()
    {
        var empty = Enumerable.Range(0, 100)
            .Select(i => new V9IdentityClusterFdr.IdentityGroup("base-" + i, 0, 0));
        var result = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatio(empty, 1);
        Assert.That(result.UpperFdr, Is.Null);
        Assert.That(result.DenominatorSupported, Is.False);
    }

    [Test]
    public void Hoeffding_cluster_bound_rejects_cap_violations_and_invalid_alpha()
    {
        var groups = new[] {
            new V9IdentityClusterFdr.IdentityGroup("base-A", 2, 0),
            new V9IdentityClusterFdr.IdentityGroup("base-B", 0, 0)
        };
        Assert.That((Action)(() => V9IdentityClusterFdr.UpperBoundForPopulationMeanRatio(groups, 1)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9IdentityClusterFdr.UpperBoundForPopulationMeanRatio(groups, 2, 0)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

}
