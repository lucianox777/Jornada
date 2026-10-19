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

    [Test]
    public void Cp_cluster_any_error_uses_exact_zero_success_bound_with_explicit_multiplicity()
    {
        // Mirrors n and accepted for the physically measured clean .95/.5
        // scenario; constructs artificial group counts to test ONLY the math.
        // It is not an operational confidence statement about that corpus.
        var groups = Enumerable.Range(0, 5556).Select(i =>
            new V9IdentityClusterFdr.IdentityGroup(
                "source-" + i, i < 1949 ? 2 : 1, 0)).ToArray();
        var one = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatioByAnyErrorCp(
            groups, 16, comparedConfigurations: 1);
        var thirtySix = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatioByAnyErrorCp(
            groups, 16, comparedConfigurations: 36);
        var hoeffding = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatio(groups, 16);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(one.TotalAccepted, Is.EqualTo(7505));
            Assert.That(one.GroupsWithAnyFalseAccepted, Is.Zero);
            Assert.That(one.UpperProbabilityOfAnyGroupErrorClopperPearson,
                Is.EqualTo(1d - Math.Pow(.025d, 1d / 5556d)).Within(1e-12));
            Assert.That(one.UpperFdrConditional,
                Is.EqualTo(.01002539d).Within(1e-5));
            Assert.That(thirtySix.UpperFdrConditional,
                Is.EqualTo(.02223022d).Within(1e-5));
            Assert.That(thirtySix.UpperFdrConditional,
                Is.GreaterThan(one.UpperFdrConditional!.Value));
            Assert.That(thirtySix.UpperFdrConditional,
                Is.LessThan(hoeffding.UpperFdr!.Value));
            Assert.That(thirtySix.ObservedFdr, Is.Zero);
            Assert.That(thirtySix.IidClusterAssumptionIndependentlyValidated, Is.False);
            Assert.That(thirtySix.OperationallyCertified, Is.False);
        }));
    }

    [Test]
    public void Cp_cluster_any_error_positive_errors_have_nonzero_upper_bound()
    {
        var groups = Enumerable.Range(0, 2000).Select(i =>
            new V9IdentityClusterFdr.IdentityGroup(
                "source-" + i, i == 0 ? 0 : 1, i == 0 ? 1 : 0)).ToArray();
        var result = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatioByAnyErrorCp(
            groups, 2, comparedConfigurations: 4);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.GroupsWithAnyFalseAccepted, Is.EqualTo(1));
            Assert.That(result.ObservedFdr, Is.EqualTo(1d / 2000d).Within(1e-12));
            Assert.That(result.UpperProbabilityOfAnyGroupErrorClopperPearson,
                Is.GreaterThan(1d / 2000d));
            Assert.That(result.UpperFdrConditional, Is.GreaterThan(result.ObservedFdr!.Value));
            Assert.That(result.OperationallyCertified, Is.False);
        }));
    }

    [Test]
    public void Cp_cluster_any_error_refuses_zero_denominator_invalid_cap_and_alpha()
    {
        var empty = Enumerable.Range(0, 100).Select(i =>
            new V9IdentityClusterFdr.IdentityGroup("source-" + i, 0, 0));
        var result = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatioByAnyErrorCp(
            empty, 1, comparedConfigurations: 1);
        Assert.That(result.UpperFdrConditional, Is.Null);
        Assert.That(result.DenominatorSupported, Is.False);
        var invalid = new[] {
            new V9IdentityClusterFdr.IdentityGroup("x", 2, 0),
            new V9IdentityClusterFdr.IdentityGroup("y", 0, 0)
        };
        Assert.That((Action)(() =>
            V9IdentityClusterFdr.UpperBoundForPopulationMeanRatioByAnyErrorCp(
                invalid, 1, 1)), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() =>
            V9IdentityClusterFdr.UpperBoundForPopulationMeanRatioByAnyErrorCp(
                invalid, 2, 0)), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That((Action)(() =>
            V9IdentityClusterFdr.UpperBoundForPopulationMeanRatioByAnyErrorCp(
                invalid, 2, 1, 0)), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

}
