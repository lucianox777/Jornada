using Jornada.Linkage.Runner;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9UnseenTfUDistributionTests
{
    [Test]
    public void Unseen_values_split_one_residual_mass_by_pinned_IBGE_and_normalize()
    {
        var train = new Dictionary<string, decimal> { ["MARIA"] = 2.5m / 3m };
        var ibge = new Dictionary<string, decimal>
        {
            ["MARIA"] = .60m, ["ZULEICA"] = .20m, ["JOANA"] = .10m
        };
        var policy = V9UnseenTfUDistribution.Fit(train, ibge, 2, .5m);
        var zu = V9UnseenTfUDistribution.Resolve(train, ibge, policy, "ZULEICA");
        var jo = V9UnseenTfUDistribution.Resolve(train, ibge, policy, "JOANA");
        var ma = V9UnseenTfUDistribution.Resolve(train, ibge, policy, "MARIA");
        Assert.Multiple(() =>
        {
            Assert.That(policy.PolicyVersion, Is.EqualTo(V9UnseenTfUDistribution.Version));
            Assert.That(policy.ReservedUProbability, Is.EqualTo(.5m / 3m));
            Assert.That(policy.UnseenReferenceMass, Is.EqualTo(.30m));
            Assert.That(zu / jo, Is.EqualTo(2m).Within(.000000001m));
            Assert.That(ma + zu + jo, Is.EqualTo(1m).Within(.000000001m));
            Assert.That(ma, Is.EqualTo(2.5m / 3m));
        });
        Assert.Throws<InvalidDataException>(() => V9UnseenTfUDistribution.Resolve(
            train, ibge, policy, "NOT_PUBLISHED"));
        Assert.Throws<InvalidDataException>(() => V9UnseenTfUDistribution.Validate(
            policy with { UnseenReferenceMass = .20m }, train, ibge));
        Assert.Throws<InvalidDataException>(() => V9UnseenTfUDistribution.Validate(
            policy with { PolicyVersion = "UNKNOWN" }, train, ibge));
    }

    [Test]
    public void A_single_bucket_must_not_be_assigned_to_every_new_name()
    {
        var train = new Dictionary<string, decimal> { ["MARIA"] = 2.5m / 3m };
        var ibge = new Dictionary<string, decimal>
        {
            ["MARIA"] = .60m, ["ZULEICA"] = .20m, ["JOANA"] = .10m,
            ["NADIA"] = .05m
        };
        var policy = V9UnseenTfUDistribution.Fit(train, ibge, 2, .5m);
        var all = ibge.Keys.Sum(name => V9UnseenTfUDistribution.Resolve(train, ibge, policy, name));
        Assert.That(all, Is.EqualTo(1m).Within(.000000001m));
        Assert.That(V9UnseenTfUDistribution.Resolve(train, ibge, policy, "ZULEICA"),
            Is.LessThan(policy.ReservedUProbability));
    }
}
