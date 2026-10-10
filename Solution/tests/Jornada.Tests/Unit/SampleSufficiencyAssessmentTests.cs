using Jornada.Linkage.Parameters.Worker;
using static Jornada.Linkage.Parameters.Worker.SampleSufficiencyAssessment;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class SampleSufficiencyAssessmentTests
{
    [Test]
    public void NoEvidence_IsIndeterminate()
    {
        var result = Evaluate(Array.Empty<Evidence>(), new[] { "SP" }, 1, 1);
        Assert.That(result.State, Is.EqualTo(Status.Indeterminate));
    }

    [Test]
    public void DeduplicatesPairsAndExcludesNonIndependentTruth()
    {
        var evidence = new[] {
            new Evidence("SP", "p1", true, true),
            new Evidence("SP", "p1", true, true),
            new Evidence("SP", "p2", true, false),
            new Evidence("SP", "p3", false, true)
        };
        var result = Evaluate(evidence, new[] { "SP" }, 2, 1);
        Assert.Multiple((NUnit.Framework.TestDelegate)(() => {
            Assert.That(result.State, Is.EqualTo(Status.Insufficient));
            Assert.That(result.Counts[0].M, Is.EqualTo(1));
            Assert.That(result.Counts[0].U, Is.EqualTo(1));
        }));
    }

    [Test]
    public void AllStrataMustPass_AndContradictionsAreExcluded()
    {
        var evidence = new[] {
            new Evidence("SP", "a", true, true),
            new Evidence("SP", "b", true, false),
            new Evidence("RJ", "c", true, true),
            new Evidence("RJ", "c", true, false)
        };
        Assert.That(Evaluate(evidence, new[] { "SP", "RJ" }, 1, 1).State,
            Is.EqualTo(Status.Insufficient));
        Assert.That(Evaluate(evidence, new[] { "SP" }, 1, 1).State,
            Is.EqualTo(Status.Sufficient));
    }

    [Test]
    public void RejectsInvalidPolicyThresholds()
    {
        Assert.Throws<ArgumentOutOfRangeException>((NUnit.Framework.TestDelegate)(() =>
            Evaluate(Array.Empty<Evidence>(), new[] { "SP" }, 0, 1)));
    }
    [Test]
    public void ThresholdBoundary_ChangesOnlyAtRequiredIndependentCount()
    {
        var observations = new[] {
            new Evidence("SP", "m1", true, true),
            new Evidence("SP", "m2", true, true),
            new Evidence("SP", "u1", true, false),
            new Evidence("SP", "u2", true, false)
        };
        Assert.That(Evaluate(observations.Take(3), new[] { "SP" }, 2, 2).State,
            Is.EqualTo(Status.Insufficient));
        Assert.That(Evaluate(observations, new[] { "SP" }, 2, 2).State,
            Is.EqualTo(Status.Sufficient));
    }

    [Test]
    public void UnrelatedStratumCannotSatisfyMissingRequiredStratum()
    {
        var observations = new[] {
            new Evidence("SP", "m1", true, true),
            new Evidence("SP", "u1", true, false)
        };
        var result = Evaluate(observations, new[] { "SP", "RJ" }, 1, 1);
        Assert.That(result.State, Is.EqualTo(Status.Insufficient));
        Assert.That(result.Counts.Single(c => c.Stratum == "RJ").M, Is.Zero);
    }

}
