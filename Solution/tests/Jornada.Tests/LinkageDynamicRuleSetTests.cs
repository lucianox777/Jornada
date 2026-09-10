using Jornada.Contracts;

namespace Jornada.Tests;

public sealed class LinkageDynamicRuleSetTests
{
    [Test]
    public void Create_IsCanonicalAcrossInputOrdering()
    {
        var a = LinkageDynamicRuleSet.Create(
            "rules-1",
            "calibrator-1",
            new[] { "birth_year", "first_name" },
            new Dictionary<string, decimal> { ["threshold"] = 3.5m, ["margin"] = 1.2m },
            "ibge-2026-09",
            new string('a', 64));

        var b = LinkageDynamicRuleSet.Create(
            "rules-1",
            "calibrator-1",
            new[] { "first_name", "birth_year" },
            new Dictionary<string, decimal> { ["margin"] = 1.2m, ["threshold"] = 3.5m },
            "ibge-2026-09",
            new string('a', 64));

        Assert.That(a.FingerprintSha256, Is.EqualTo(b.FingerprintSha256));
        Assert.That(a.BlockingFields, Is.EqualTo(new[] { "birth_year", "first_name" }));
    }

    [Test]
    public void Create_ChangesFingerprintWhenRulesChange()
    {
        var a = LinkageDynamicRuleSet.Create(
            "rules-1", "calibrator-1", new[] { "first_name" },
            new Dictionary<string, decimal> { ["threshold"] = 3.5m });
        var b = LinkageDynamicRuleSet.Create(
            "rules-2", "calibrator-1", new[] { "first_name" },
            new Dictionary<string, decimal> { ["threshold"] = 3.6m });

        Assert.That(a.FingerprintSha256, Is.Not.EqualTo(b.FingerprintSha256));
    }

    [Test]
    public void Create_RequiresCompleteIbgeIdentity()
    {
        Assert.Throws<ArgumentException>(() => LinkageDynamicRuleSet.Create(
            "rules-1", "calibrator-1", new[] { "first_name" },
            Array.Empty<KeyValuePair<string, decimal>>(),
            ibgeSourceVersion: "ibge-2026-09"));
    }
}
