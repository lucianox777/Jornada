using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlConditionedNominalSupportAuditTests
{
    private static readonly V9SqlConditionedNominalSupportAudit.Candidate[] SqlTrain =
    [
        new(0, 1, "TRAIN", "base-1", "base-1",
            "ANA SILVA", "ANA SILVA", "MARIA SOUZA", "MARIA SOUZA"),
        new(2, 3, "TRAIN", "base-1", "base-1",
            "ANA SILVA", "ANA SILVA", "MARIA SOUZA", "MARIA SOUZA"),
        new(4, 5, "TRAIN", "base-2", "base-3",
            "ANA SILVA", "ANA SILVA", "MARIA SOUZA", "MARIA SOUZA"),
        new(6, 7, "TRAIN", "base-3", "base-2",
            "ANA SILVA", "ANA SILVA", "MARIA SOUZA", "MARIA SOUZA"),
        new(8, 9, "TRAIN", "base-2", "base-4",
            "ANA SILVA", "JOSE PEREIRA", null, "MARIA SOUZA")
    ];

    [Test]
    public void Produces_all_81_conditional_states_with_group_support_not_pair_degree()
    {
        var report = V9SqlConditionedNominalSupportAudit.Analyze(SqlTrain);
        var exact = report.Cells.Single(x =>
            x.PersonLevel == CompositeNominalLevelV9.ExactFull
            && x.MotherLevel == CompositeNominalLevelV9.ExactFull);
        var missing = report.Cells.Single(x =>
            x.PersonLevel == CompositeNominalLevelV9.Other
            && x.MotherLevel == CompositeNominalLevelV9.Missing);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.Partition, Is.EqualTo("TRAIN"));
            Assert.That(report.Cells, Has.Count.EqualTo(81));
            Assert.That(report.SqlCandidatePairs, Is.EqualTo(5));
            Assert.That(report.SqlCandidateM, Is.EqualTo(2));
            Assert.That(report.SqlCandidateU, Is.EqualTo(3));
            Assert.That(exact.MCount, Is.EqualTo(2));
            Assert.That(exact.UCount, Is.EqualTo(2));
            Assert.That(exact.DistinctMBasePeople, Is.EqualTo(1));
            Assert.That(exact.DistinctUBasePairs, Is.EqualTo(1));
            Assert.That(exact.DistinctUBasePeople, Is.EqualTo(2));
            Assert.That(missing.MCount, Is.Zero);
            Assert.That(missing.UCount, Is.EqualTo(1));
            Assert.That(report.Cells.Sum(x => x.MCount), Is.EqualTo(2));
            Assert.That(report.Cells.Sum(x => x.UCount), Is.EqualTo(3));
            Assert.That(report.Status, Does.Contain("NO_INDEPENDENT_MU"));
        }));
        var serialized = JsonSerializer.Serialize(report);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(serialized, Does.Not.Contain("base-1"));
            Assert.That(serialized, Does.Not.Contain("ANA SILVA"));
            Assert.That(serialized, Does.Not.Contain("MARIA SOUZA"));
            Assert.That(serialized, Does.Not.Contain("Candidate"));
        }));
    }

    [Test]
    public void Fails_closed_on_duplicate_SQL_pair_or_leaked_validation_candidate()
    {
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlConditionedNominalSupportAudit.Analyze([SqlTrain[0], SqlTrain[0]])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlConditionedNominalSupportAudit.Analyze(
                [SqlTrain[0], SqlTrain[2] with { Partition = "VALIDATION" }])));
    }

    [Test]
    public void Fails_closed_without_bilateral_SQL_conditioned_support()
    {
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlConditionedNominalSupportAudit.Analyze([SqlTrain[0]])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlConditionedNominalSupportAudit.Analyze([SqlTrain[2]])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlConditionedNominalSupportAudit.Analyze([])));
    }
}
