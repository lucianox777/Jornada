using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class LinkageTermFrequencyCalibrationAuditTests
{
    private static NominalTermFrequencySnapshot Snapshot()
        => NominalTermFrequencySnapshot.Create(new[]
        {
            new NominalTermFrequencyEntry(
                NominalTermFrequencySnapshot.PersonFirstNameAttribute, "MARIA", 600, 1000, .6m),
            new NominalTermFrequencyEntry(
                NominalTermFrequencySnapshot.PersonFirstNameAttribute, "ZULEICA", 10, 1000, .01m),
            new NominalTermFrequencyEntry(
                NominalTermFrequencySnapshot.MotherFirstNameAttribute, "ANA", 500, 1000, .5m)
        });

    private static Dictionary<string, decimal> Parameters(
        decimal weight = 1m,
        decimal personMExact = .25m,
        decimal personUExact = .25m,
        decimal personMHigh = .25m,
        decimal personUHigh = .25m,
        decimal motherMExact = .8m,
        decimal motherUExact = .01m,
        decimal motherMHigh = .1m,
        decimal motherUHigh = .1m)
        => new(StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.TermFrequencyScoring] = 1m,
            [LinkageParameterCatalog.TermFrequencyWeight] = weight,
            [LinkageParameterCatalog.TermFrequencyMinimumU] = .01m,
            ["M_NOME_EXACT"] = personMExact,
            ["U_NOME_EXACT"] = personUExact,
            ["M_NOME_HIGH"] = personMHigh,
            ["U_NOME_HIGH"] = personUHigh,
            ["M_NOME_MAE_EXACT"] = motherMExact,
            ["U_NOME_MAE_EXACT"] = motherUExact,
            ["M_NOME_MAE_HIGH"] = motherMHigh,
            ["U_NOME_MAE_HIGH"] = motherUHigh
        };

    [Test]
    public void P0_ReportsAllFactorsForCommonFirstNameWithoutRewritingScore()
    {
        var before = Parameters();
        var original = before.ToDictionary(x => x.Key, x => x.Value);
        var result = LinkageTermFrequencyCalibrationAudit.TryAssess(before, Snapshot());
        Assert.That(result, Is.Not.Null);
        var name = result!.Single(x => x.Attribute == "NOME");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(name.MExact, Is.EqualTo(.25m));
            Assert.That(name.UExact, Is.EqualTo(.25m));
            Assert.That(name.MaximumPublishedFirstNameFrequency, Is.EqualTo(.6m));
            Assert.That(name.ExactBaseLogOdds, Is.EqualTo(0d).Within(1e-12));
            Assert.That(name.HighBaseLogOdds, Is.EqualTo(0d).Within(1e-12));
            Assert.That(name.WorstTfAdjustment,
                Is.EqualTo(Math.Log(.25d / .6d)).Within(1e-12));
            Assert.That(name.WorstExactMinusHigh,
                Is.EqualTo(Math.Log(.25d / .6d)).Within(1e-12));
            Assert.That(name.CouldInvert, Is.True);
            Assert.That(before, Is.EquivalentTo(original));
        }));
        // No term values or personal identifiers become diagnostic fields.
        Assert.That(typeof(LinkageTermFrequencyCalibrationAudit.AttributeAssessment)
            .GetProperties().Select(p => p.Name),
            Has.None.Matches<string>(p => p.Contains("Token") || p.Contains("Person")));
    }

    [Test]
    public void StrongBaseFsCanExceedTfPenalty_WithoutCallingThatCalibrated()
    {
        var result = LinkageTermFrequencyCalibrationAudit.TryAssess(
            Parameters(personMExact: .8m, personUExact: .01m,
                       personMHigh: .1m, personUHigh: .1m), Snapshot());
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Single(x => x.Attribute == "NOME").CouldInvert, Is.False);
        Assert.That(result.Single(x => x.Attribute == "NOME_MAE").CouldInvert, Is.False);
    }

    [Test]
    public void MotherIsAuditedIndependentlyAndCanHaveNegativeMargin()
    {
        var result = LinkageTermFrequencyCalibrationAudit.TryAssess(
            Parameters(personMExact: .8m, personUExact: .01m,
                       personMHigh: .1m, personUHigh: .1m,
                       motherMExact: .2m, motherUExact: .2m,
                       motherMHigh: .2m, motherUHigh: .2m), Snapshot());
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Single(x => x.Attribute == "NOME").CouldInvert, Is.False);
        var mother = result.Single(x => x.Attribute == "NOME_MAE");
        Assert.That(mother.CouldInvert, Is.True);
        Assert.That(mother.WorstTfAdjustment,
            Is.EqualTo(Math.Log(.2d / .5d)).Within(1e-12));
    }

    [Test]
    public void WeightZeroMakesNoFrequencyAdjustment()
    {
        var result = LinkageTermFrequencyCalibrationAudit.TryAssess(
            Parameters(weight: 0m, personMExact: .6m, personUExact: .2m,
                       personMHigh: .1m, personUHigh: .2m), Snapshot());
        Assert.That(result, Is.Not.Null);
        var name = result!.Single(x => x.Attribute == "NOME");
        Assert.That(name.WorstTfAdjustment, Is.EqualTo(0d).Within(1e-12));
        Assert.That(name.WorstExactLogOdds,
            Is.EqualTo(name.ExactBaseLogOdds).Within(1e-12));
    }

    [Test]
    public void MissingAndInvalidInputsDoNotBlockOperationalPath()
    {
        var parameters = Parameters();
        parameters.Remove("U_NOME_EXACT");
        Assert.That(LinkageTermFrequencyCalibrationAudit.TryAssess(parameters, Snapshot()), Is.Null);
        parameters = Parameters();
        parameters[LinkageParameterCatalog.TermFrequencyWeight] = -1m;
        Assert.That(LinkageTermFrequencyCalibrationAudit.TryAssess(parameters, Snapshot()), Is.Null);
        parameters = Parameters();
        parameters[LinkageParameterCatalog.TermFrequencyMinimumU] = 0m;
        Assert.That(LinkageTermFrequencyCalibrationAudit.TryAssess(parameters, Snapshot()), Is.Null);
        parameters = Parameters();
        parameters[LinkageParameterCatalog.TermFrequencyScoring] = 0m;
        Assert.That(LinkageTermFrequencyCalibrationAudit.TryAssess(parameters, Snapshot()), Is.Null);
    }
}
