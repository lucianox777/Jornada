using Jornada.Linkage.Parameters.Worker;
using static Jornada.Linkage.Parameters.Worker.SampleSufficiencyAssessment;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class SampleSufficiencyAssessmentTests
{
    [Test]
    public void RepeatedPairsWithoutIndependentGroupIdentifiersCannotCertify()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true),
            new Evidence("SP", "m2", true, true),
            new Evidence("SP", "u1", true, false)
        };
        var result = Evaluate(evidence, new[] { "SP" }, 2, 1);
        Assert.That(result.State, Is.EqualTo(Status.Insufficient));
        Assert.That(result.Reasons.Any(r => r.Contains("missing group identifiers", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void IndependentGroupDiversityIsRequiredBeyondPairCount()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true, "person-A"),
            new Evidence("SP", "m2", true, true, "person-A"),
            new Evidence("SP", "u1", true, false, "person-A")
        };
        Assert.That(Evaluate(evidence, new[] { "SP" }, 2, 1, 2).State, Is.EqualTo(Status.Insufficient));
    }

    [Test]
    public void DuplicatePairWithConflictingGroupProvenanceCannotCertify()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true, "person-A"),
            new Evidence("SP", "m1", true, true, "person-B"),
            new Evidence("SP", "u1", true, false, "person-C")
        };
        var result = Evaluate(evidence, new[] { "SP" }, 1, 1);
        Assert.That(result.State, Is.EqualTo(Status.Insufficient));
        Assert.That(result.Reasons.Any(r => r.Contains("missing group identifiers", StringComparison.Ordinal)), Is.True);
    }

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
            new Evidence("SP", "p1", true, true, "g1"),
            new Evidence("SP", "p1", true, true),
            new Evidence("SP", "p2", true, false, "g2"),
            new Evidence("SP", "p3", false, true)
        };
        var result = Evaluate(evidence, new[] { "SP" }, 2, 1);
        Assert.Multiple((System.Action)(() => {
            Assert.That(result.State, Is.EqualTo(Status.Insufficient));
            Assert.That(result.Counts[0].M, Is.EqualTo(1));
            Assert.That(result.Counts[0].U, Is.EqualTo(1));
        }));
    }

    [Test]
    public void AllStrataMustPass_AndContradictionsAreExcluded()
    {
        var evidence = new[] {
            new Evidence("SP", "a", true, true, "g1"),
            new Evidence("SP", "b", true, false, "g2"),
            new Evidence("RJ", "c", true, true),
            new Evidence("RJ", "c", true, false)
        };
        Assert.That(Evaluate(evidence, new[] { "SP", "RJ" }, 1, 1).State,
            Is.EqualTo(Status.Indeterminate));
        Assert.That(Evaluate(evidence, new[] { "SP" }, 1, 1).State,
            Is.EqualTo(Status.Sufficient));
    }

    [Test]
    public void RejectsInvalidPolicyThresholds()
    {
        Assert.Throws<ArgumentOutOfRangeException>((System.Action)(() =>
            Evaluate(Array.Empty<Evidence>(), new[] { "SP" }, 0, 1)));
    }
    [Test]
    public void ThresholdBoundary_ChangesOnlyAtRequiredIndependentCount()
    {
        var observations = new[] {
            new Evidence("SP", "m1", true, true, "g1"),
            new Evidence("SP", "m2", true, true, "g2"),
            new Evidence("SP", "u1", true, false, "g3"),
            new Evidence("SP", "u2", true, false, "g4")
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

    [Test]
    public void ContradictoryIndependentLabelsFailClosedEvenWhenCountsMeetMinimum()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true),
            new Evidence("SP", "u1", true, false),
            new Evidence("SP", "conflict", true, true),
            new Evidence("SP", "conflict", true, false)
        };
        var result = Evaluate(evidence, new[] { "SP" }, 1, 1);
        Assert.That(result.State, Is.EqualTo(Status.Indeterminate));
        Assert.That(result.Reasons.Any(x => x.Contains("contradictory", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void EvidenceOutsideRequiredStrataDoesNotCertifyAssessment()
    {
        var unrelated = new[] { new Evidence("RJ", "m", true, true), new Evidence("RJ", "u", true, false) };
        var result = Evaluate(unrelated, new[] { "SP" }, 1, 1);
        Assert.That(result.State, Is.EqualTo(Status.Indeterminate));
        Assert.That(result.Counts.Single().M, Is.Zero);
        Assert.That(result.Counts.Single().U, Is.Zero);
    }
}
