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
        Assert.Multiple(() => {
            Assert.That(result.State, Is.EqualTo(Status.Insufficient));
            Assert.That(result.Counts[0].M, Is.EqualTo(1));
            Assert.That(result.Counts[0].U, Is.EqualTo(1));
        });
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
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Evaluate(Array.Empty<Evidence>(), new[] { "SP" }, 0, 1));
    }
}
