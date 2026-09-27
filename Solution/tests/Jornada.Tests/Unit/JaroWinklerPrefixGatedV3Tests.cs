using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class JaroWinklerPrefixGatedV3Tests
{
    [Test]
    public void LowJaro_DoesNotReceivePrefixBonus_AndV1IsUnchanged()
    {
        // One common leading character; Jaro = 0.5, V1 Winkler = 0.55.
        Assert.That(IdentityComparison.JaroWinkler("ABCD", "AXYZ"), Is.EqualTo(0.55d).Within(1e-12));
        Assert.That(IdentityComparison.JaroWinklerPrefixGated("ABCD", "AXYZ"), Is.EqualTo(0.5d).Within(1e-12));
    }

    [Test]
    public void HighJaro_RetainsExistingPrefixBonus()
    {
        var legacy = IdentityComparison.JaroWinkler("MARIA", "MARIE");
        var gated = IdentityComparison.JaroWinklerPrefixGated("MARIA", "MARIE");
        Assert.That(gated, Is.EqualTo(legacy).Within(1e-12));
        Assert.That(gated, Is.GreaterThan(0.7d));
    }

    [Test]
    public void LegacyAlias_AndV2_AreNotRewired()
    {
        Assert.That(IdentityComparison.CompareName("ABCD", "AXYZ"),
            Is.EqualTo(IdentityComparison.CompareNameV1("ABCD", "AXYZ")));
        Assert.That(IdentityComparison.CompareName("ABCD", "AXYZ",
            NameComparisonContract.PtBrContentTokenGuardV2),
            Is.EqualTo(IdentityComparison.CompareNameV2("ABCD", "AXYZ")));
        Assert.That(IdentityComparison.CompareName("ABCD", "AXYZ",
            NameComparisonContract.WholeNameJaroWinklerPrefixGatedV3),
            Is.EqualTo(IdentityComparison.CompareNameV3("ABCD", "AXYZ")));
    }

    [Test]
    public void V3_PreservesNullAndExactSemantics()
    {
        Assert.That(IdentityComparison.CompareNameV3(null, "MARIA"), Is.EqualTo(NameComparisonState.LOW));
        Assert.That(IdentityComparison.CompareNameV3("Maria", "MARIA"), Is.EqualTo(NameComparisonState.EXACT));
    }
}
