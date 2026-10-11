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
        Assert.That(result.State, Is.EqualTo(Status.Indeterminate));
        Assert.That(result.Reasons.Any(r => r.Contains("contradictory independent group provenance", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void DuplicatePairWithMissingAndPresentProvenanceIsIndeterminate()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true, "person-A"),
            new Evidence("SP", "m1", true, true),
            new Evidence("SP", "u1", true, false, "person-B")
        };
        var result = Evaluate(evidence, new[] { "SP" }, 1, 1);
        Assert.That(result.State, Is.EqualTo(Status.Indeterminate));
        Assert.That(result.Reasons.Any(r => r.Contains("contradictory independent group provenance", StringComparison.Ordinal)), Is.True);
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
            new Evidence("SP", "p1", true, true, "g1"),
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
    [Test]
    public void UnmatchedGroupDiversityCannotCompensateForMatchedGroupDeficit()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true, "person-A"),
            new Evidence("SP", "m2", true, true, "person-A"),
            new Evidence("SP", "u1", true, false, "person-B"),
            new Evidence("SP", "u2", true, false, "person-C")
        };
        var result = Evaluate(evidence, new[] { "SP" }, 2, 2, 2);
        Assert.That(result.State, Is.EqualTo(Status.Insufficient));
        Assert.That(result.Reasons.Any(r => r.Contains("independent groups m=1, u=2", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void MatchedGroupDiversityCannotCompensateForUnmatchedGroupDeficit()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true, "person-A"),
            new Evidence("SP", "m2", true, true, "person-B"),
            new Evidence("SP", "u1", true, false, "person-C"),
            new Evidence("SP", "u2", true, false, "person-C")
        };
        var result = Evaluate(evidence, new[] { "SP" }, 2, 2, 2);
        Assert.That(result.State, Is.EqualTo(Status.Insufficient));
        Assert.That(result.Reasons.Any(r => r.Contains("independent groups m=2, u=1", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void SingleUseEvidenceSequenceIsNotEnumeratedTwice()
    {
        var observations = new SingleUseEvidence(new[] {
            new Evidence("SP", "m1", true, true, "g1"),
            new Evidence("SP", "m2", true, true, "g2"),
            new Evidence("SP", "u1", true, false, "g3"),
            new Evidence("SP", "u2", true, false, "g4")
        });
        var result = Evaluate(observations, new[] { "SP" }, 2, 2, 2);
        Assert.That(result.State, Is.EqualTo(Status.Sufficient));
        Assert.That(result.Counts.Single().M, Is.EqualTo(2));
        Assert.That(result.Counts.Single().U, Is.EqualTo(2));
    }

    [Test]
    public void WhitespaceVariantsOfSamePairDoNotInflateCounts()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true, "group-A"),
            new Evidence("SP", " m1 ", true, true, " group-A "),
            new Evidence("SP", "u1", true, false, "group-B")
        };
        var result = Evaluate(evidence, new[] { "SP" }, 2, 1);
        Assert.That(result.State, Is.EqualTo(Status.Insufficient));
        Assert.That(result.Counts.Single().M, Is.EqualTo(1));
        Assert.That(result.Counts.Single().U, Is.EqualTo(1));
    }

    [Test]
    public void WhitespaceCannotHideContradictoryProvenance()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true, "group-A"),
            new Evidence("SP", " m1 ", true, true, "group-B"),
            new Evidence("SP", "u1", true, false, "group-C")
        };
        var result = Evaluate(evidence, new[] { "SP" }, 1, 1);
        Assert.That(result.State, Is.EqualTo(Status.Indeterminate));
        Assert.That(result.Reasons.Any(r => r.Contains("contradictory independent group provenance", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void MissingIndependentPairKeyBlocksCertificationEvenWhenOtherCountsPass()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true, "group-A"),
            new Evidence("SP", "u1", true, false, "group-B"),
            new Evidence("SP", "  ", true, true, "group-C")
        };
        var result = Evaluate(evidence, new[] { "SP" }, 1, 1);
        Assert.That(result.State, Is.EqualTo(Status.Indeterminate));
        Assert.That(result.Reasons.Any(r => r.Contains("missing independent pair key", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void MissingPairKeyInUnrequestedStratumDoesNotBlockRequestedStratum()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true, "group-A"),
            new Evidence("SP", "u1", true, false, "group-B"),
            new Evidence("RJ", "", true, true, "group-C")
        };
        Assert.That(Evaluate(evidence, new[] { "SP" }, 1, 1).State, Is.EqualTo(Status.Sufficient));
    }

    [Test]
    public void WhitespaceVariantOfRequiredStratumCannotBeSilentlyExcluded()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true, "group-A"),
            new Evidence("SP", "u1", true, false, "group-B"),
            new Evidence(" SP ", "m2", true, true, "group-C")
        };
        var result = Evaluate(evidence, new[] { "SP" }, 1, 1);
        Assert.That(result.State, Is.EqualTo(Status.Indeterminate));
        Assert.That(result.Reasons.Any(r => r.Contains("noncanonical required stratum", StringComparison.Ordinal)), Is.True);
        Assert.That(result.Counts.Single().M, Is.EqualTo(1));
    }

    [Test]
    public void MissingIndependentStratumCannotBeSilentlyExcluded()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true, "group-A"),
            new Evidence("SP", "u1", true, false, "group-B"),
            new Evidence("", "m2", true, true, "group-C")
        };
        var result = Evaluate(evidence, new[] { "SP" }, 1, 1);
        Assert.That(result.State, Is.EqualTo(Status.Indeterminate));
        Assert.That(result.Reasons.Any(r => r.Contains("missing or noncanonical required stratum", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void UnrelatedStratumAndNonIndependentWhitespaceDoNotContaminateRequiredStratum()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true, "group-A"),
            new Evidence("SP", "u1", true, false, "group-B"),
            new Evidence(" RJ ", "m2", true, true, "group-C"),
            new Evidence(" SP ", "m3", false, true, "group-D")
        };
        Assert.That(Evaluate(evidence, new[] { "SP" }, 1, 1).State, Is.EqualTo(Status.Sufficient));
    }

    [TestCase(" SP ")]
    [TestCase("  ")]
    public void NoncanonicalRequiredStratumPolicyIsRejected(string requestedStratum)
    {
        var evidence = new[] {
            new Evidence(requestedStratum, "m1", true, true, "group-A"),
            new Evidence(requestedStratum, "u1", true, false, "group-B")
        };
        Assert.Throws<ArgumentException>((System.Action)(() => Evaluate(evidence, new[] { requestedStratum }, 1, 1)));
    }

    [Test]
    public void DuplicateRequiredStratumPolicyIsRejectedRatherThanCertified()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true, "group-A"),
            new Evidence("SP", "u1", true, false, "group-B")
        };
        Assert.Throws<ArgumentException>((System.Action)(() => Evaluate(evidence, new[] { "SP", "SP" }, 1, 1)));
    }

    [Test]
    public void ManyPairsFromOnlyTwoGroupsCannotMeetEffectiveSampleMinimum()
    {
        // Pair count alone is not the effective independent sample size.
        var evidence = Enumerable.Range(0, 100)
            .Select(i => new Evidence("SP", $"m-{i}", true, true, $"m-group-{i % 2}"))
            .Concat(Enumerable.Range(0, 100)
                .Select(i => new Evidence("SP", $"u-{i}", true, false, $"u-group-{i % 2}")))
            .ToArray();

        var result = Evaluate(evidence, new[] { "SP" }, 100, 100, 2);

        Assert.That(result.State, Is.EqualTo(Status.Insufficient));
        Assert.That(result.Counts.Single().M, Is.EqualTo(100));
        Assert.That(result.Counts.Single().U, Is.EqualTo(100));
        Assert.That(result.Reasons.Any(r => r.Contains(
            "independent groups m=2, u=2; required m=100, u=100",
            StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void IndependentGroupsMeetingBothClassMinimumsCanPass()
    {
        var evidence = Enumerable.Range(0, 3)
            .Select(i => new Evidence("SP", $"m-{i}", true, true, $"m-person-{i}"))
            .Concat(Enumerable.Range(0, 3)
                .Select(i => new Evidence("SP", $"u-{i}", true, false, $"u-person-{i}")));

        var result = Evaluate(evidence, new[] { "SP" }, 3, 3, 2);
        Assert.That(result.State, Is.EqualTo(Status.Sufficient));
    }

    [Test]
    public void InvalidIndependentGroupMinimumCannotDisableDiversityGate()
    {
        Assert.Throws<ArgumentOutOfRangeException>((System.Action)(() =>
            Evaluate(Array.Empty<Evidence>(), new[] { "SP" }, 1, 1, 0)));
    }

    [Test]
    public void DuplicateRequiredStrataAreRejectedAsMalformedPolicy()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true, "person-A"),
            new Evidence("SP", "u1", true, false, "person-B")
        };
        Assert.Throws<ArgumentException>((System.Action)(() =>
            Evaluate(evidence, new[] { "SP", "SP" }, 2, 1)));
    }

    [Test]
    public void SelfDerivedIndependentGroupCannotCertify()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true, "m1"),
            new Evidence("SP", "u1", true, false, "u1")
        };
        var result = Evaluate(evidence, new[] { "SP" }, 1, 1);
        Assert.That(result.State, Is.EqualTo(Status.Indeterminate));
        Assert.That(result.Reasons.Any(r => r.Contains("equals pair key", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void NoncanonicalRequestedStratumIsRejectedAsInvalidPolicy()
    {
        var evidence = new[] {
            new Evidence("SP", "m1", true, true, "person-A"),
            new Evidence("SP", "u1", true, false, "person-B")
        };
        Assert.Throws<ArgumentException>((System.Action)(() =>
            Evaluate(evidence, new[] { " SP " }, 1, 1)));
    }

    [Test]
    public void InvalidPolicyReportsTheCorrectParameterName()
    {
        var evidence = Array.Empty<Evidence>();
        var m = Assert.Throws<ArgumentOutOfRangeException>((System.Action)(() =>
            Evaluate(evidence, new[] { "SP" }, 0, 1)));
        var u = Assert.Throws<ArgumentOutOfRangeException>((System.Action)(() =>
            Evaluate(evidence, new[] { "SP" }, 1, 0)));
        var groups = Assert.Throws<ArgumentOutOfRangeException>((System.Action)(() =>
            Evaluate(evidence, new[] { "SP" }, 1, 1, 0)));
        Assert.Multiple((System.Action)(() => {
            Assert.That(m!.ParamName, Is.EqualTo("minimumM"));
            Assert.That(u!.ParamName, Is.EqualTo("minimumU"));
            Assert.That(groups!.ParamName, Is.EqualTo("minimumIndependentGroups"));
        }));
    }

    [Test]
    public void ReusedPairAcrossRequiredStrataBlocksCertification()
    {
        var evidence = new[] {
            new Evidence("SP", "shared", true, true, "sp-m"),
            new Evidence("SP", "sp-u", true, false, "sp-u-group"),
            new Evidence("RJ", " shared ", true, true, "rj-m"),
            new Evidence("RJ", "rj-u", true, false, "rj-u-group")
        };
        var result = Evaluate(evidence, new[] { "SP", "RJ" }, 1, 1);
        Assert.That(result.State, Is.EqualTo(Status.Indeterminate));
        Assert.That(result.Reasons.Any(r => r.Contains("multiple required strata", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void DistinctPairsAcrossRequiredStrataRemainCertifiable()
    {
        var evidence = new[] {
            new Evidence("SP", "sp-m", true, true, "sp-g1"),
            new Evidence("SP", "sp-u", true, false, "sp-g2"),
            new Evidence("RJ", "rj-m", true, true, "rj-g1"),
            new Evidence("RJ", "rj-u", true, false, "rj-g2")
        };
        Assert.That(Evaluate(evidence, new[] { "SP", "RJ" }, 1, 1).State, Is.EqualTo(Status.Sufficient));
    }

    private sealed class SingleUseEvidence : IEnumerable<Evidence>
    {
        private readonly Evidence[] _items;
        private bool _used;

        public SingleUseEvidence(Evidence[] items) => _items = items;

        public IEnumerator<Evidence> GetEnumerator()
        {
            if (_used) throw new InvalidOperationException("Evidence sequence was enumerated more than once.");
            _used = true;
            return ((IEnumerable<Evidence>)_items).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
