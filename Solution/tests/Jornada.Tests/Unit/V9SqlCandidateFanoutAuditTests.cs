using System.Text.Json;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlCandidateFanoutAuditTests
{
    private static readonly V9SqlCandidateFanoutAudit.Observation[] Rows =
    [
        new(0, "TRAIN", "G1", "A", true),
        new(1, "TRAIN", "G2", "A", true),
        new(2, "TRAIN", "G3", "B", true),
        new(3, "TRAIN", "G2", "C", true),
        new(4, "TRAIN", "G1", "D", true),
        new(5, "TRAIN", "G1", "E", true), // zero SQL candidates
        new(6, "VALIDATION", "G1", "F", true),
        new(7, "VALIDATION", "G2", "G", true),
        new(8, "TEST", "G1", "H", true),
        new(9, "TEST", "G2", "I", true),
        new(10, "TRAIN", "G1", "INELIGIBLE", false)
    ];

    private static V9SqlCandidateFanoutAudit.Collector Prepared()
    {
        var collector = new V9SqlCandidateFanoutAudit.Collector(Rows);
        collector.AddCandidate(0, 1, "TRAIN"); // M
        collector.AddCandidate(0, 2, "TRAIN"); // U
        collector.AddCandidate(0, 3, "TRAIN"); // U
        collector.AddCandidate(2, 4, "TRAIN"); // U
        collector.AddCandidate(3, 4, "TRAIN"); // U
        return collector;
    }

    [Test]
    public void Counts_every_eligible_endpoint_including_zero_and_reconciles_pair_endpoints()
    {
        var report = Prepared().Build();
        var train = report.Splits.Single(x => x.Partition == "TRAIN");
        var test = report.Splits.Single(x => x.Partition == "TEST");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.Splits, Has.Count.EqualTo(3));
            Assert.That(train.EligibleObservations, Is.EqualTo(6));
            Assert.That(train.WithCandidates, Is.EqualTo(5));
            Assert.That(train.WithoutCandidates, Is.EqualTo(1));
            Assert.That(train.CandidateTruePairs, Is.EqualTo(1));
            Assert.That(train.CandidateFalsePairs, Is.EqualTo(4));
            Assert.That(train.DistinctFalseBasePairs, Is.EqualTo(4));
            Assert.That(train.TotalEndpointIncidences, Is.EqualTo(10));
            Assert.That(train.FalseEndpointIncidences, Is.EqualTo(8));
            Assert.That(train.P50CandidatesPerObservation, Is.EqualTo(2));
            Assert.That(train.P95CandidatesPerObservation, Is.EqualTo(3));
            Assert.That(train.P99CandidatesPerObservation, Is.EqualTo(3));
            Assert.That(train.MaxCandidatesPerObservation, Is.EqualTo(3));
            Assert.That(train.P95FalseCandidatesPerObservation, Is.EqualTo(2));
            Assert.That(train.P99FalseCandidatesPerObservation, Is.EqualTo(2));
            Assert.That(train.MaxFalseCandidatesPerObservation, Is.EqualTo(2));
            Assert.That(test.EligibleObservations, Is.EqualTo(2));
            Assert.That(test.WithoutCandidates, Is.EqualTo(2));
            Assert.That(test.P99CandidatesPerObservation, Is.Zero);
            Assert.That(report.PercentileDefinition, Does.Contain("INCLUDING_ZERO"));
        }));
        var json = JsonSerializer.Serialize(report);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(json, Does.Not.Contain("INELIGIBLE"));
            Assert.That(json, Does.Not.Contain("BasePersonId"));
            Assert.That(json, Does.Not.Contain("Gestor"));
        }));
    }

    [Test]
    public void Rejects_duplicate_edge_cross_partition_same_gestor_and_ineligible_endpoint()
    {
        var duplicate = Prepared();
        Assert.Throws<InvalidDataException>((Action)(() =>
            duplicate.AddCandidate(0, 1, "TRAIN")));
        var crossPartition = Prepared();
        Assert.Throws<InvalidDataException>((Action)(() =>
            crossPartition.AddCandidate(0, 8, "TRAIN")));
        var sameGestor = Prepared();
        Assert.Throws<InvalidDataException>((Action)(() =>
            sameGestor.AddCandidate(0, 4, "TRAIN")));
        var ineligible = Prepared();
        Assert.Throws<InvalidDataException>((Action)(() =>
            ineligible.AddCandidate(0, 10, "TRAIN")));
    }

    [Test]
    public void Duplicate_observation_id_or_empty_eligible_frame_fails_closed()
    {
        Assert.Throws<InvalidDataException>((Action)(() =>
            new V9SqlCandidateFanoutAudit.Collector([Rows[0], Rows[0]])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            new V9SqlCandidateFanoutAudit.Collector([Rows[10]])));
    }

    [Test]
    public void Distinct_base_pairs_do_not_treat_duplicate_observation_edges_as_independent_people()
    {
        V9SqlCandidateFanoutAudit.Observation[] rows =
        [
            new(0, "TRAIN", "G1", "A", true),
            new(1, "TRAIN", "G2", "B", true),
            new(2, "TRAIN", "G3", "A", true),
            new(3, "VALIDATION", "G1", "C", true),
            new(4, "TEST", "G1", "D", true)
        ];
        var c = new V9SqlCandidateFanoutAudit.Collector(rows);
        c.AddCandidate(0, 1, "TRAIN");
        c.AddCandidate(1, 2, "TRAIN");
        var split = c.Build().Splits.Single(x => x.Partition == "TRAIN");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(split.CandidateFalsePairs, Is.EqualTo(2));
            Assert.That(split.DistinctFalseBasePairs, Is.EqualTo(1));
        }));
    }
}
