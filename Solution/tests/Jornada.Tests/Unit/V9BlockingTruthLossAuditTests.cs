using System.Text.Json;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9BlockingTruthLossAuditTests
{
    [Test]
    public void Reconciles_eligible_and_retrieved_M_in_exact_disjoint_buckets_without_leaking_ids()
    {
        V9BlockingTruthLossAudit.Pair[] eligible =
        [
            new(0, 1, "TRAIN", false, false, false, false, false), // triple divergence; lost
            new(2, 3, "TRAIN", false, true, true, false, true),    // mother missing; recovered
            new(4, 5, "VALIDATION", false, true, false, true, true),
            new(6, 7, "TEST", false, false, false, false, false),  // triple divergence; recovered
            new(8, 9, "TEST", false, false, false, false, false),  // triple divergence; lost
            new(10, 11, "TEST", true, false, true, false, true)   // person missing; lost
        ];
        var recovered = new HashSet<(int A, int B)>
        {
            (2, 3), (4, 5), (6, 7)
        };

        var report = V9BlockingTruthLossAudit.Analyze(eligible, recovered);
        var train = report.Partitions.Single(x => x.Partition == "TRAIN");
        var test = report.Partitions.Single(x => x.Partition == "TEST");
        var tripleTest = report.Cells.Single(x => x.Partition == "TEST"
            && x.NameState == "DIFFERENT" && x.MotherState == "DIFFERENT"
            && x.BirthState == "DIFFERENT");
        var missingMother = report.Cells.Single(x => x.Partition == "TRAIN"
            && x.NameState == "EXACT" && x.MotherState == "MISSING"
            && x.BirthState == "EXACT");

        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.Cells, Has.Count.EqualTo(54));
            Assert.That(train.EligibleTruePairs, Is.EqualTo(2));
            Assert.That(train.RetrievedTruePairs, Is.EqualTo(1));
            Assert.That(train.SimultaneousNameMotherBirthDisagreementLost, Is.EqualTo(1));
            Assert.That(test.EligibleTruePairs, Is.EqualTo(3));
            Assert.That(test.RetrievedTruePairs, Is.EqualTo(1));
            Assert.That(test.SimultaneousNameMotherBirthDisagreementLost, Is.EqualTo(1));
            Assert.That(tripleTest.EligibleTruePairs, Is.EqualTo(2));
            Assert.That(tripleTest.RetrievedTruePairs, Is.EqualTo(1));
            Assert.That(tripleTest.LostTruePairs, Is.EqualTo(1));
            Assert.That(missingMother.RetrievedTruePairs, Is.EqualTo(1));
            Assert.That(report.Cells.Sum(x => x.EligibleTruePairs), Is.EqualTo(eligible.Length));
            Assert.That(report.Cells.Sum(x => x.RetrievedTruePairs), Is.EqualTo(recovered.Count));
        }));
        var json = JsonSerializer.Serialize(report);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(json, Does.Not.Contain("LowerObservationId"));
            Assert.That(json, Does.Not.Contain("HigherObservationId"));
            Assert.That(json, Does.Not.Contain("NameMissing"));
            Assert.That(json, Does.Contain("DESCRIPTIVE_ONLY"));
        }));
    }

    [Test]
    public void Fails_closed_when_SQL_true_candidate_is_outside_independent_denominator()
    {
        V9BlockingTruthLossAudit.Pair[] eligible =
        [
            new(0, 1, "TRAIN", false, true, false, true, true)
        ];
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9BlockingTruthLossAudit.Analyze(
                eligible, new HashSet<(int A, int B)> { (0, 2) })));
    }

    [Test]
    public void Fails_closed_on_duplicate_pair_or_ambiguous_observed_state()
    {
        var pair = new V9BlockingTruthLossAudit.Pair(
            0, 1, "TEST", false, true, false, true, true);
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9BlockingTruthLossAudit.Analyze([pair, pair],
                new HashSet<(int A, int B)>())));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9BlockingTruthLossAudit.Analyze([pair with { NameMissing = true }],
                new HashSet<(int A, int B)>())));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9BlockingTruthLossAudit.Analyze([pair with { Partition = "OTHER" }],
                new HashSet<(int A, int B)>())));
    }
}
