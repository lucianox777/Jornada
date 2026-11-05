using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9TrainCandidatePriorDiagnosticTests
{
    private static readonly string EdgeSha = new('a', 64);
    private static V9FsAdaptiveCandidatePair Pair(string a, string b, bool match,
        string partition = "TRAIN") => new(
            partition, a, b, match, "ANA", "ANA", "MARIA", "MARIA",
            new DateOnly(1980, 1, 2), new DateOnly(1980, 1, 2), 4);

    [Test]
    public void Measures_distinct_SQL_observation_edges_and_repeated_base_contributions_separately()
    {
        var rows = new[] {
            Pair("A", "A", true), Pair("A", "A", true), Pair("B", "B", true),
            Pair("A", "B", false), Pair("B", "A", false), Pair("A", "C", false)
        };
        var r = V9TrainCandidatePriorDiagnostic.Analyze(rows, 4, EdgeSha);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(r.TrainCandidateObservationPairs, Is.EqualTo(6));
            Assert.That(r.TrainTrueObservationPairs, Is.EqualTo(3));
            Assert.That(r.TrainFalseObservationPairs, Is.EqualTo(3));
            Assert.That(r.RawPairCandidateMatchShare, Is.EqualTo(.5m));
            Assert.That(r.DistinctMBaseIdentities, Is.EqualTo(2));
            Assert.That(r.DistinctUnorderedUBasePairs, Is.EqualTo(2));
            Assert.That(r.RepeatedMBaseIdentityContributions, Is.EqualTo(1));
            Assert.That(r.RepeatedUBasePairContributions, Is.EqualTo(1));
            Assert.That(r.MaximumMObservationPairsFromOneBase, Is.EqualTo(2));
            Assert.That(r.MaximumUObservationPairIncidencesOnOneBase, Is.EqualTo(3));
            Assert.That(r.BaseCollapsedShareSensitivityOnly, Is.EqualTo(.5m));
            Assert.That(r.PreBlockingBaseIdentityCount, Is.EqualTo(4));
            Assert.That(r.BaseIdentitiesRepresentedInTrainPairs, Is.EqualTo(3));
            Assert.That(r.SqlPhysicalObservationEdgesDeduplicated, Is.True);
            Assert.That(r.BaseCollapsedShareIsCalibratedPrior, Is.False);
            Assert.That(r.TransportabilityToCpfAbsentPopulationEstablished, Is.False);
            Assert.That(r.IndependentIdentitySupportEstablished, Is.False);
            Assert.That(r.ChangesFittedPriorOrDecisions, Is.False);
        }));
    }

    [Test]
    public void Concentrated_base_repetition_changes_pair_prior_not_unique_base_sensitivity()
    {
        var rows = new[] { Pair("A", "A", true), Pair("B", "B", true),
            Pair("A", "B", false), Pair("B", "C", false) };
        var a = V9TrainCandidatePriorDiagnostic.Analyze(rows, 4, EdgeSha);
        var b = V9TrainCandidatePriorDiagnostic.Analyze(
            rows.Concat(Enumerable.Repeat(Pair("A", "A", true), 8)).ToArray(), 4, EdgeSha);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(a.RawPairCandidateMatchShare, Is.EqualTo(.5m));
            Assert.That(b.RawPairCandidateMatchShare, Is.GreaterThan(.8m));
            Assert.That(a.BaseCollapsedShareSensitivityOnly, Is.EqualTo(b.BaseCollapsedShareSensitivityOnly));
            Assert.That(b.RepeatedMBaseIdentityContributions, Is.EqualTo(8));
        }));
    }

    [Test]
    public void Fails_closed_on_test_validation_bad_truth_sha_and_missing_class()
    {
        var good = new[] { Pair("A", "A", true), Pair("A", "B", false) };
        foreach (var invalid in new[] {
            good.Append(Pair("C", "C", true, "TEST")).ToArray(),
            good.Append(Pair("C", "C", true, "VALIDATION")).ToArray(),
            good.Append(Pair("A", "B", true)).ToArray(),
            good.Append(Pair("A", "A", false)).ToArray()
        })
            Assert.That((Action)(() => V9TrainCandidatePriorDiagnostic.Analyze(invalid, 4, EdgeSha)),
                Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9TrainCandidatePriorDiagnostic.Analyze(good, 1, EdgeSha)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9TrainCandidatePriorDiagnostic.Analyze(good, 4, "bad-sha")),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9TrainCandidatePriorDiagnostic.Analyze(
            [Pair("A", "A", true)], 4, EdgeSha)), Throws.TypeOf<InvalidDataException>());
    }
}
