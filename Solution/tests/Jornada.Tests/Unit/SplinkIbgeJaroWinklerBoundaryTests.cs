using Jornada.Contracts;

namespace Jornada.Tests.Unit;

/// <summary>
/// Explicitly characterizes V1 comparison semantics observed in the 2026-09-27
/// external IBGE × Splink 4.0.17 replay. These are compatibility tests, not
/// evidence that the external library implements the same Jaro-Winkler variant.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class SplinkIbgeJaroWinklerBoundaryTests
{
    [Test]
    public void V1_AppliesPrefixBonus_WhenJaroBaseIsBelowPointSeven()
    {
        const string left = "JOSE POSSOBOM";
        const string right = "JOSE SILVA";
        // The four-character shared prefix increases the C# V1 score even
        // though its Jaro base is below 0.7. DuckDB's variant classified this
        // exact synthetic pair LOW, whereas Jornada V1 classified it MEDIUM.
        var score = IdentityComparison.JaroWinkler(left, right);
        var inferredJaroBase = (score - 0.4d) / 0.6d;
        Assert.Multiple(() =>
        {
            Assert.That(inferredJaroBase, Is.LessThan(0.7d));
            Assert.That(score, Is.GreaterThanOrEqualTo(0.80d));
            Assert.That(score, Is.LessThan(0.92d));
            Assert.That(IdentityComparison.CompareNameV1(left, right),
                Is.EqualTo(NameComparisonState.MEDIUM));
        });
    }

    [TestCase("JOSIANE SANTOS", "ELISA SANTOS", NameComparisonState.LOW)]
    [TestCase("MARIA SILVA", "MARIA VIOL", NameComparisonState.MEDIUM)]
    [TestCase("JOSE POSSOBOM", "JOSE SILVA", NameComparisonState.MEDIUM)]
    public void V1_KeepsObservedBoundaryStates(
        string left, string right, NameComparisonState expected)
    {
        Assert.That(IdentityComparison.CompareNameV1(left, right), Is.EqualTo(expected));
        Assert.That(IdentityComparison.CompareName(left, right), Is.EqualTo(expected));
    }
}
