using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

/// <summary>
/// Independent examples for each of the four (side, nominal-unit) TF slots.
/// Expected applicability and published-frequency outcomes are literal test
/// fixtures, not derived by calling the production selector/classifier.
/// This is NOT an independent FS/TF4 numerical scoring oracle.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class V9IndependentTf4UnitCoverageOracleTests
{
    [TestCase("MARIA SILVA", "MARIA SILVA", NominalFrequencyUnitV9.FirstName)]
    [TestCase("MARIA SILVA", "MARIA SOUZA", NominalFrequencyUnitV9.FirstName)]
    [TestCase("MARIA SILVA", "ANA SILVA", NominalFrequencyUnitV9.LastContentSurname)]
    [TestCase("MARIA SILVA", "ANA SOUZA", null)]
    public void Independent_nominal_examples_select_exactly_one_unit(
        string left, string right, NominalFrequencyUnitV9? expected)
    {
        var level = CompositeNominalComparisonV9.Classify(left, right);
        Assert.That(V9NominalTfUnitSelection.ForLevel(level), Is.EqualTo(expected));
    }

    [Test]
    public void Four_slots_abstain_only_when_the_selected_positive_term_is_unpublished()
    {
        // Person and mother are distinct frozen slots, even when their
        // published marginal dictionaries happen to contain the same tokens.
        foreach (var side in new[] { CompositeNominalTfSideV9.Person, CompositeNominalTfSideV9.Mother })
        foreach (var unit in new[] { NominalFrequencyUnitV9.FirstName, NominalFrequencyUnitV9.LastContentSurname })
        {
            var left = "MARIA SILVA";
            var right = unit == NominalFrequencyUnitV9.FirstName ? "MARIA SOUZA" : "ANA SILVA";
            var term = unit == NominalFrequencyUnitV9.FirstName ? "MARIA" : "SILVA";
            var published = new Dictionary<string, decimal> { [term] = .01m };
            var unpublished = new Dictionary<string, decimal>();
            Assert.That(V9NominalTfTermCoverage.MissingPublishedFrequency(unit, left, right, published),
                Is.False, $"published {side}/{unit}");
            Assert.That(V9NominalTfTermCoverage.MissingPublishedFrequency(unit, left, right, unpublished),
                Is.True, $"unpublished {side}/{unit}");
            // Inapplicable positive TF must not invent an abstention.
            var unrelated = unit == NominalFrequencyUnitV9.FirstName
                ? NominalFrequencyUnitV9.LastContentSurname
                : NominalFrequencyUnitV9.FirstName;
            Assert.That(V9NominalTfTermCoverage.MissingPublishedFrequency(unrelated, left, right, unpublished),
                Is.False, $"inapplicable {side}/{unit}");
        }
    }
}
