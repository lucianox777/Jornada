using System.Text.Json;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlTrainValidationUnionFanoutAuditTests
{
    private static readonly V9SqlTrainValidationUnionFanoutAudit.Observation[] Rows =
    [
        new(0, "TRAIN", "G1", true), new(1, "TRAIN", "G2", true),
        new(3, "TRAIN", "G2", true), new(9, "TRAIN", "G2", true),
        new(12, "TRAIN", "G1", true), new(15, "TRAIN", "G2", true),
        new(16, "TRAIN", "G3", true), // zero-hit, still in denominator
        new(4, "VALIDATION", "G1", true), new(5, "VALIDATION", "G2", true),
        new(7, "VALIDATION", "G2", true), new(11, "VALIDATION", "G2", true),
        new(14, "VALIDATION", "G1", true),
        new(17, "VALIDATION", "G3", true), // zero-hit
        new(18, "TRAIN", "G3", false) // ineligible
    ];

    private static readonly V9SqlTrainValidationUnionSearch.Pair[] Baseline =
    [
        new(0, 1, "TRAIN", true), new(0, 3, "TRAIN", false),
        new(4, 5, "VALIDATION", true), new(4, 7, "VALIDATION", false)
    ];

    private static readonly V9SqlTrainValidationUnionSearch.Extension[] Extensions =
    [
        new("A", [
            new(0, 9, "TRAIN", true),
            new(4, 11, "VALIDATION", true)]),
        new("B", [
            new(9, 12, "TRAIN", true),
            new(0, 15, "TRAIN", false),
            new(11, 14, "VALIDATION", true)]),
        new("C", [
            new(0, 9, "TRAIN", true),
            new(4, 11, "VALIDATION", true)]), // overlapping pass A
        new("D", []), new("E", []), new("F", [])
    ];

    private static V9SqlTrainValidationUnionFanoutAudit.Report Analyze(
        IReadOnlyList<V9SqlTrainValidationUnionFanoutAudit.Observation>? observations = null,
        IReadOnlyList<V9SqlTrainValidationUnionSearch.Extension>? extensions = null)
    {
        var passSet = extensions ?? Extensions;
        var search = V9SqlTrainValidationUnionSearch.Analyze(
            Baseline, passSet, 3, 3, 500);
        return V9SqlTrainValidationUnionFanoutAudit.Analyze(
            observations ?? Rows, Baseline, passSet, search, 500);
    }

    [Test]
    public void Full_56_unions_have_exact_zero_inclusive_endpoint_degrees()
    {
        var report = Analyze();
        var train = report.Splits.Single(x =>
            x.Signature == "A||B" && x.Partition == "TRAIN");
        var validation = report.Splits.Single(x =>
            x.Signature == "A||B" && x.Partition == "VALIDATION");
        var overlap = report.Splits.Single(x =>
            x.Signature == "A||C" && x.Partition == "TRAIN");

        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.EvaluatedUnions, Is.EqualTo(56));
            Assert.That(report.Splits, Has.Count.EqualTo(112));
            Assert.That(report.EligibleTrainObservations, Is.EqualTo(7));
            Assert.That(report.EligibleValidationObservations, Is.EqualTo(6));
            Assert.That(report.MayPromoteBlockingPolicy, Is.False);
            Assert.That(report.PartitionScope, Does.Contain("TEST_FORBIDDEN"));
            Assert.That(train.EligibleObservations, Is.EqualTo(7));
            Assert.That(train.ObservationsWithCandidates, Is.EqualTo(6));
            Assert.That(train.ObservationsWithoutCandidates, Is.EqualTo(1));
            Assert.That(train.M, Is.EqualTo(3));
            Assert.That(train.U, Is.EqualTo(2));
            Assert.That(train.TotalEndpointIncidences, Is.EqualTo(10));
            Assert.That(train.FalseEndpointIncidences, Is.EqualTo(4));
            Assert.That(train.P95Candidates, Is.EqualTo(4));
            Assert.That(train.P99Candidates, Is.EqualTo(4));
            Assert.That(train.MaxCandidates, Is.EqualTo(4));
            Assert.That(train.P95FalseCandidates, Is.EqualTo(2));
            Assert.That(train.P99FalseCandidates, Is.EqualTo(2));
            Assert.That(train.MaxFalseCandidates, Is.EqualTo(2));
            Assert.That(train.CandidateDegreeHistogram.Sum(
                x => x.DegreeValue * x.ObservationCount), Is.EqualTo(10));
            Assert.That(train.CandidateDegreeHistogram.First().DegreeValue, Is.Zero);
            Assert.That(train.CandidateDegreeHistogram.First().ObservationCount,
                Is.EqualTo(1));
            Assert.That(validation.M, Is.EqualTo(3));
            Assert.That(validation.U, Is.EqualTo(1));
            Assert.That(validation.ObservationsWithoutCandidates, Is.EqualTo(1));
            Assert.That(overlap.M, Is.EqualTo(2)); // overlapping A and C not double counted
            Assert.That(overlap.U, Is.EqualTo(1));
        }));
        var json = JsonSerializer.Serialize(report);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(json, Does.Not.Contain("BasePersonId"));
            Assert.That(json, Does.Not.Contain("Gestor"));
            Assert.That(json, Does.Not.Contain("ObservationId"));
            Assert.That(json, Does.Not.Contain("Pairs"));
        }));
    }

    [Test]
    public void Rejects_TEST_in_observation_frame_and_ineligible_endpoints()
    {
        Assert.Throws<InvalidDataException>((Action)(() =>
            Analyze([.. Rows, new(99, "TEST", "G1", true)])));
        var modified = Rows.Select(x => x.Id == 0
            ? x with { Eligible = false } : x).ToArray();
        Assert.Throws<InvalidDataException>((Action)(() => Analyze(modified)));
    }

    [Test]
    public void Rejects_duplicate_observation_id_and_incompatible_gestor()
    {
        Assert.Throws<InvalidDataException>((Action)(() =>
            Analyze([.. Rows, Rows[0]])));
        var sameGestor = Rows.Select(x => x.Id == 1
            ? x with { Gestor = "G1" } : x).ToArray();
        Assert.Throws<InvalidDataException>((Action)(() => Analyze(sameGestor)));
    }
}
