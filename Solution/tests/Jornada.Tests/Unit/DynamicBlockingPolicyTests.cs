using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class DynamicBlockingPolicyTests
{
    [Test]
    public void CurrentPolicy_IsDeterministicAndVersioned()
    {
        var a = DynamicBlockingPolicy.CreateCurrent(true, 1);
        var b = DynamicBlockingPolicy.CreateCurrent(true, 1);

        Assert.Multiple(() =>
        {
            Assert.That(a.PolicyVersion, Is.EqualTo(DynamicBlockingPolicy.CurrentPolicyVersion));
            Assert.That(a.BlockingPlanVersion, Is.EqualTo(BirthBlockingPlan.Version));
            Assert.That(a.FingerprintSha256(), Is.EqualTo(b.FingerprintSha256()));
            Assert.That(a.FingerprintSha256(), Has.Length.EqualTo(64));
        });
    }

    [Test]
    public void PolicyFingerprint_ChangesWhenRuleConfigurationChanges()
    {
        var a = DynamicBlockingPolicy.CreateCurrent(true, 1);
        var b = DynamicBlockingPolicy.CreateCurrent(true, 2);
        Assert.That(a.FingerprintSha256(), Is.Not.EqualTo(b.FingerprintSha256()));
    }

    [Test]
    public void Policy_FailsClosedForPartialExternalProvenance()
    {
        var current = DynamicBlockingPolicy.CreateCurrent(true, 1);
        var invalid = current with
        {
            ExternalNameFrequencySource = "IBGE_NOMES_NO_BRASIL",
            ExternalNameFrequencyVersion = "2026",
            ExternalNameFrequencyFingerprint = null
        };
        Assert.Throws<ArgumentException>(() => invalid.Validate());
    }

    [Test]
    public void Policy_MatchFiltersDisabledPasses()
    {
        var current = DynamicBlockingPolicy.CreateCurrent(true, 1);
        var exactOnly = (current with { EnabledPasses = BirthBlockingPass.ExactDate }).Validate();
        var plan = BirthBlockingPlan.Create(new DateOnly(1982,4,10), "Maria", "Ana", true, 1);

        Assert.Multiple(() =>
        {
            Assert.That(exactOnly.Match(plan, new DateOnly(1982,4,10), "Maria", "Ana"),
                Is.EqualTo(BirthBlockingPass.ExactDate));
            Assert.That(exactOnly.Match(plan, new DateOnly(1982,4,11), "Maria", "Ana"),
                Is.EqualTo(BirthBlockingPass.None));
        });
    }
}
