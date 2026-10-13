using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlConditionalMuIdentifiabilityAuditTests
{
    private static V9SqlConditionedNominalSupportAudit.Report Nominal()
    {
        var cells = (
            from person in Enum.GetValues<CompositeNominalLevelV9>()
            from mother in Enum.GetValues<CompositeNominalLevelV9>()
            select new V9SqlConditionedNominalSupportAudit.Cell(
                person, mother, 0, 0, 0, 0, 0)).ToArray();
        for (var i = 0; i < cells.Length; i++)
        {
            var x = cells[i];
            if (x.PersonLevel == CompositeNominalLevelV9.ExactFull
                && x.MotherLevel == CompositeNominalLevelV9.ExactFull)
                cells[i] = x with { MCount = 1, UCount = 1,
                    DistinctMBasePeople = 1, DistinctUBasePairs = 1,
                    DistinctUBasePeople = 2 };
            if (x.PersonLevel == CompositeNominalLevelV9.ExactFirst
                && x.MotherLevel == CompositeNominalLevelV9.Other)
                cells[i] = x with { MCount = 1, DistinctMBasePeople = 1 };
            if (x.PersonLevel == CompositeNominalLevelV9.Missing
                && x.MotherLevel == CompositeNominalLevelV9.Missing)
                cells[i] = x with { UCount = 1, DistinctUBasePairs = 1,
                    DistinctUBasePeople = 2 };
        }
        return new(V9SqlConditionedNominalSupportAudit.Version,
            V9SqlConditionedNominalSupportAudit.Status, "TRAIN", 4, 2, 2,
            2, 2, 1, cells);
    }

    private static V9SqlConditionedBirthSupportAudit.Report Birth()
    {
        var cells = BirthDateSemanticEvidence.States.Select(state =>
            new V9SqlConditionedBirthSupportAudit.Cell(
                state, 0, 0, 0, 0, 0)).ToArray();
        for (var i = 0; i < cells.Length; i++)
        {
            var x = cells[i];
            if (x.SemanticBirthState == BirthDateSemanticEvidence.Exact)
                cells[i] = x with { MCount = 1, UCount = 1,
                    DistinctMBasePeople = 1, DistinctUBasePairs = 1,
                    DistinctUBasePeople = 2 };
            else if (x.SemanticBirthState == BirthDateSemanticEvidence.DayMonthSwap)
                cells[i] = x with { MCount = 1, DistinctMBasePeople = 1 };
            else if (x.SemanticBirthState == BirthDateSemanticEvidence.OtherDisagreement)
                cells[i] = x with { UCount = 1, DistinctUBasePairs = 1,
                    DistinctUBasePeople = 2 };
        }
        return new(V9SqlConditionedBirthSupportAudit.Version,
            V9SqlConditionedBirthSupportAudit.Status,
            "TRAIN", 4, 2, 2, 1, 1, 1, 1, 2, 2, 1, cells);
    }

    private static V9SyntheticBaseFrequencyAudit.Report BaseTrain() =>
        new(V9SyntheticBaseFrequencyAudit.Version,
            V9SyntheticBaseFrequencyAudit.Status, "TRAIN",
            8, 4, "test-source-fingerprint",
            Array.Empty<V9SyntheticBaseFrequencyAudit.UnitAudit>());

    [Test]
    public void Exposes_empirical_conditioned_MU_but_never_converts_empty_cells_to_neutral()
    {
        var report = V9SqlConditionalMuIdentifiabilityAudit.Analyze(
            Nominal(), Birth(), BaseTrain());
        var both = report.JointCells.Single(x =>
            x.PersonLevel == CompositeNominalLevelV9.ExactFull
            && x.MotherLevel == CompositeNominalLevelV9.ExactFull);
        var noM = report.JointCells.Single(x =>
            x.PersonLevel == CompositeNominalLevelV9.Missing
            && x.MotherLevel == CompositeNominalLevelV9.Missing);
        var noU = report.JointCells.Single(x =>
            x.PersonLevel == CompositeNominalLevelV9.ExactFirst
            && x.MotherLevel == CompositeNominalLevelV9.Other);
        var neither = report.JointCells.Single(x =>
            x.PersonLevel == CompositeNominalLevelV9.ReversedFirstLast
            && x.MotherLevel == CompositeNominalLevelV9.Other);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.MayPromoteModelParameters, Is.False);
            Assert.That(report.Status, Does.StartWith("INCONCLUSIVE"));
            Assert.That(report.UniqueTrainBasePeople, Is.EqualTo(4));
            Assert.That(report.JointCells, Has.Count.EqualTo(81));
            Assert.That(report.BirthCells, Has.Count.EqualTo(7));
            Assert.That(report.EmpiricalPairMatchShareNotPopulationPrior, Is.EqualTo(0.5m));
            Assert.That(report.JointCellsWithBothObserved, Is.EqualTo(1));
            Assert.That(report.JointCellsWithoutAnyObservation, Is.EqualTo(78));
            Assert.That(report.JointCellsMissingEitherClass, Is.EqualTo(80));
            Assert.That(both.EmpiricalMShareOfSqlCandidateM, Is.EqualTo(.5m));
            Assert.That(both.EmpiricalUShareOfSqlCandidateU, Is.EqualTo(.5m));
            Assert.That(both.DescriptiveObservedPairRatio, Is.EqualTo(1m));
            Assert.That(noM.DescriptiveObservedPairRatio, Is.Null);
            Assert.That(noM.Identifiability, Is.EqualTo("UNOBSERVED_M_NOT_NEUTRAL"));
            Assert.That(noU.DescriptiveObservedPairRatio, Is.Null);
            Assert.That(noU.Identifiability, Is.EqualTo("UNOBSERVED_U_NOT_NEUTRAL"));
            Assert.That(neither.DescriptiveObservedPairRatio, Is.Null);
            Assert.That(neither.Identifiability, Is.EqualTo("UNOBSERVED_BOTH_NOT_NEUTRAL"));
            Assert.That(report.JointCells.Sum(x => x.EmpiricalMShareOfSqlCandidateM), Is.EqualTo(1m));
            Assert.That(report.JointCells.Sum(x => x.EmpiricalUShareOfSqlCandidateU), Is.EqualTo(1m));
        }));
        var json = JsonSerializer.Serialize(report);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(json, Does.Not.Contain("BasePersonId"));
            Assert.That(json, Does.Not.Contain("test-source-fingerprint"));
            Assert.That(json, Does.Not.Contain("THRESHOLD"));
            Assert.That(json, Does.Not.Contain("BayesFactor"));
        }));
    }

    [Test]
    public void Fails_closed_on_cross_frame_or_invalid_base_group_support()
    {
        var nominal = Nominal();
        var birth = Birth();
        var population = BaseTrain();
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlConditionalMuIdentifiabilityAudit.Analyze(
                nominal, birth with { SqlCandidateU = 3 }, population)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlConditionalMuIdentifiabilityAudit.Analyze(
                nominal with { Partition = "TEST" }, birth, population)));
        var invalid = nominal.Cells.ToArray();
        invalid[0] = invalid[0] with { DistinctMBasePeople = 10 };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlConditionalMuIdentifiabilityAudit.Analyze(
                nominal with { Cells = invalid }, birth, population)));
    }

    [Test]
    public void Rejects_fabricated_observational_class_sums()
    {
        var nominal = Nominal();
        var cells = nominal.Cells.ToArray();
        cells[0] = cells[0] with { MCount = cells[0].MCount + 1 };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlConditionalMuIdentifiabilityAudit.Analyze(
                nominal with { Cells = cells }, Birth(), BaseTrain())));
    }
}
