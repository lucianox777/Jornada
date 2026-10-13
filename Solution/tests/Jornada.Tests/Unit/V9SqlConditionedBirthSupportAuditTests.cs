using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlConditionedBirthSupportAuditTests
{
    private static readonly DateOnly Day = new(1980, 3, 4);
    private static readonly DateOnly Swapped = new(1980, 4, 3);

    private static readonly V9SqlConditionedBirthSupportAudit.Candidate[] SqlTrain =
    [
        new(0, 1, "TRAIN", "base-1", "base-1", Day, Day),
        new(2, 3, "TRAIN", "base-1", "base-1", Day, Swapped),
        new(4, 5, "TRAIN", "base-2", "base-3", Day, Day),
        new(6, 7, "TRAIN", "base-3", "base-2", Day, Day),
        new(8, 9, "TRAIN", "base-4", "base-5", Day, Swapped),
        new(10, 11, "TRAIN", "base-1", "base-1", Day, Day)
    ];

    [Test]
    public void Has_seven_exhaustive_semantic_states_and_same_frame_binary_reconciliation()
    {
        var report = V9SqlConditionedBirthSupportAudit.Analyze(SqlTrain);
        var exact = report.SemanticCells.Single(x =>
            x.SemanticBirthState == BirthDateSemanticEvidence.Exact);
        var swapped = report.SemanticCells.Single(x =>
            x.SemanticBirthState == BirthDateSemanticEvidence.DayMonthSwap);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.Partition, Is.EqualTo("TRAIN"));
            Assert.That(report.SemanticCells, Has.Count.EqualTo(7));
            Assert.That(report.SemanticCells.Select(x => x.SemanticBirthState),
                Is.EquivalentTo(BirthDateSemanticEvidence.States));
            Assert.That(report.SqlCandidatePairs, Is.EqualTo(6));
            Assert.That(report.SqlCandidateM, Is.EqualTo(3));
            Assert.That(report.SqlCandidateU, Is.EqualTo(3));
            Assert.That(report.BirthExactM, Is.EqualTo(2));
            Assert.That(report.BirthExactU, Is.EqualTo(2));
            Assert.That(report.BirthDifferentM, Is.EqualTo(1));
            Assert.That(report.BirthDifferentU, Is.EqualTo(1));
            Assert.That(exact.MCount, Is.EqualTo(2));
            Assert.That(exact.UCount, Is.EqualTo(2));
            Assert.That(exact.DistinctMBasePeople, Is.EqualTo(1));
            Assert.That(exact.DistinctUBasePairs, Is.EqualTo(1));
            Assert.That(exact.DistinctUBasePeople, Is.EqualTo(2));
            Assert.That(swapped.MCount, Is.EqualTo(1));
            Assert.That(swapped.UCount, Is.EqualTo(1));
            Assert.That(report.SemanticCells.Sum(x => x.MCount),
                Is.EqualTo(report.SqlCandidateM));
            Assert.That(report.SemanticCells.Sum(x => x.UCount),
                Is.EqualTo(report.SqlCandidateU));
            Assert.That(report.StatesWithBothMAndU, Is.EqualTo(2));
            Assert.That(report.Status, Does.Contain("NOT_CALIBRATED"));
        }));
        var serialized = JsonSerializer.Serialize(report);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(serialized, Does.Not.Contain("base-1"));
            Assert.That(serialized, Does.Not.Contain("BaseA"));
            Assert.That(serialized, Does.Not.Contain("BirthA"));
        }));
    }

    [Test]
    public void Rejects_repeated_SQL_edge_and_validation_test_contamination()
    {
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlConditionedBirthSupportAudit.Analyze([SqlTrain[0], SqlTrain[0]])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlConditionedBirthSupportAudit.Analyze(
                [SqlTrain[0], SqlTrain[2] with { Partition = "VALIDATION" }])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlConditionedBirthSupportAudit.Analyze(
                [SqlTrain[0], SqlTrain[2] with { Partition = "TEST" }])));
    }

    [Test]
    public void Rejects_empty_or_one_class_SQL_frame_without_inventing_m_u()
    {
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlConditionedBirthSupportAudit.Analyze([])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlConditionedBirthSupportAudit.Analyze([SqlTrain[0]])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlConditionedBirthSupportAudit.Analyze([SqlTrain[2]])));
    }
}
