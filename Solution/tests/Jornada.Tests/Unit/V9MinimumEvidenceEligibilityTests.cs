using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9MinimumEvidenceEligibilityTests
{
    private static readonly DateTime Dob = new(1984, 7, 13);

    [TestCase("MARIA SILVA", "ANA SOUZA", null, "PM")]
    [TestCase("MARIA SILVA", null, "1984-07-13", "PD")]
    [TestCase(null, "ANA SOUZA", "1984-07-13", "MD")]
    [TestCase("MARIA SILVA", "ANA SOUZA", "1984-07-13", "PMD")]
    public void Two_or_three_valid_fields_are_eligible(string? person, string? mother, string? date, string expected)
    {
        var result = V9MinimumEvidenceEligibility.Evaluate(person, mother,
            date is null ? null : DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Multiple(() =>
        {
            Assert.That(result.Eligible, Is.True);
            Assert.That(result.MustAbstain, Is.False);
            Assert.That(result.Combination, Is.EqualTo(expected));
        });
    }

    [TestCase(null, null)]
    [TestCase("MARIA SILVA", null)]
    [TestCase(null, "ANA SOUZA")]
    [TestCase("NÃO INFORMADO", "ANA SOUZA")]
    [TestCase("12345", "ANA SOUZA")]
    public void Less_than_two_valid_fields_abstain(string? person, string? mother)
    {
        var result = V9MinimumEvidenceEligibility.Evaluate(person, mother, null);
        Assert.That(result.MustAbstain, Is.True);
        Assert.That(result.Eligible, Is.False);
    }

    [Test]
    public void Placeholder_and_sentinel_birth_do_not_count()
    {
        Assert.That(V9MinimumEvidenceEligibility.Evaluate("MARIA", "N/A", new DateTime(1900, 1, 1)).Eligible, Is.False);
        Assert.That(V9MinimumEvidenceEligibility.Evaluate("MARIA", "ANA", new DateTime(1900, 1, 1)).Eligible, Is.True);
        Assert.That(V9MinimumEvidenceEligibility.Evaluate(null, "ANA", DateTime.UtcNow.AddDays(1)).Eligible, Is.False);
        Assert.That(V9MinimumEvidenceEligibility.Evaluate(null, "ANA", Dob).Combination, Is.EqualTo("MD"));
    }
}
