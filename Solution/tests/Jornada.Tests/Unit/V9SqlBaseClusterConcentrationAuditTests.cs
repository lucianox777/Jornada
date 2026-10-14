using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlBaseClusterConcentrationAuditTests
{
    private static readonly DateOnly Birth = new(1984, 4, 9);
    private static readonly V9SqlConditionedNominalSupportAudit.Candidate[] NominalPairs =
    [
        new(0, 1, "TRAIN", "BASE-X", "BASE-X", "ANA SILVA", "ANA SILVA", "MARIA COSTA", "MARIA COSTA"),
        new(2, 3, "TRAIN", "BASE-X", "BASE-X", "ANA SILVA", "ANA SILVA", "MARIA COSTA", "MARIA COSTA"),
        new(4, 5, "TRAIN", "BASE-X", "BASE-Z", "ANA SILVA", "ANA SILVA", "MARIA COSTA", "MARIA COSTA"),
        new(6, 7, "TRAIN", "BASE-Z", "BASE-X", "ANA SILVA", "ANA SILVA", "MARIA COSTA", "MARIA COSTA"),
        new(8, 9, "TRAIN", "BASE-X", "BASE-Y", "ANA SILVA", "ANA SILVA", "MARIA COSTA", "MARIA COSTA")
    ];

    private static readonly V9SqlConditionedBirthSupportAudit.Candidate[] BirthPairs =
        NominalPairs.Select(x => new V9SqlConditionedBirthSupportAudit.Candidate(
            x.A, x.B, x.Partition, x.BaseA, x.BaseB, Birth, Birth)).ToArray();

    private static V9SqlBaseClusterConcentrationAudit.Report Analyze(
        IReadOnlyList<V9SqlConditionedNominalSupportAudit.Candidate>? nominal = null,
        IReadOnlyList<V9SqlConditionedBirthSupportAudit.Candidate>? birth = null,
        int bases = 3) =>
        V9SqlBaseClusterConcentrationAudit.Analyze(
            nominal ?? NominalPairs, birth ?? BirthPairs,
            V9SqlConditionedNominalSupportAudit.Analyze(NominalPairs),
            V9SqlConditionedBirthSupportAudit.Analyze(BirthPairs), bases);

    [Test]
    public void Reports_concentration_by_base_not_observation_count_or_independent_ESS()
    {
        var report = Analyze();
        var m = report.Cells.Single(x => x.Domain == "NOMINAL_JOINT"
            && x.PersonLevel == CompositeNominalLevelV9.ExactFull
            && x.MotherLevel == CompositeNominalLevelV9.ExactFull
            && x.ClassLabel == "M");
        var u = report.Cells.Single(x => x.Domain == "NOMINAL_JOINT"
            && x.PersonLevel == CompositeNominalLevelV9.ExactFull
            && x.MotherLevel == CompositeNominalLevelV9.ExactFull
            && x.ClassLabel == "U");
        var birthM = report.Cells.Single(x => x.Domain == "BIRTH_SEMANTIC"
            && x.BirthState == BirthDateSemanticEvidence.Exact
            && x.ClassLabel == "M");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.Cells, Has.Count.EqualTo(176));
            Assert.That(report.SqlCandidateM, Is.EqualTo(2));
            Assert.That(report.SqlCandidateU, Is.EqualTo(3));
            Assert.That(report.EstimatesIndependentEss, Is.False);
            Assert.That(report.MayPromoteModelParameters, Is.False);
            Assert.That(report.Status, Does.StartWith("INCONCLUSIVE"));
            Assert.That(m.ObservationPairs, Is.EqualTo(2));
            Assert.That(m.DistinctBaseContributors, Is.EqualTo(1));
            Assert.That(m.BaseContributionIncidences, Is.EqualTo(2));
            Assert.That(m.SquaredBaseContributionIncidences, Is.EqualTo(4));
            Assert.That(m.MaxBaseContributionIncidences, Is.EqualTo(2));
            Assert.That(m.KishEquivalentBaseContributorsNotIndependentEss, Is.EqualTo(1m));
            Assert.That(u.ObservationPairs, Is.EqualTo(3));
            Assert.That(u.DistinctBaseContributors, Is.EqualTo(3));
            Assert.That(u.BaseContributionIncidences, Is.EqualTo(6));
            Assert.That(u.SquaredBaseContributionIncidences, Is.EqualTo(14));
            Assert.That(u.MaxBaseContributionIncidences, Is.EqualTo(3));
            Assert.That(u.LargestBaseContributionShare, Is.EqualTo(.5m));
            Assert.That(u.KishEquivalentBaseContributorsNotIndependentEss,
                Is.EqualTo(36m / 14m).Within(.0000000000000000000000001m));
            Assert.That(birthM.ObservationPairs, Is.EqualTo(2));
        }));
        var json = JsonSerializer.Serialize(report);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(json, Does.Not.Contain("BASE-X"));
            Assert.That(json, Does.Not.Contain("BASE-Z"));
            Assert.That(json, Does.Not.Contain("ANA SILVA"));
            Assert.That(json, Does.Not.Contain("BirthA"));
            Assert.That(json, Does.Not.Contain("BaseA"));
        }));
    }

    [Test]
    public void Missing_cells_preserve_null_concentration_not_false_neutral()
    {
        var report = Analyze();
        var empty = report.Cells.Single(x => x.Domain == "NOMINAL_JOINT"
            && x.PersonLevel == CompositeNominalLevelV9.Missing
            && x.MotherLevel == CompositeNominalLevelV9.Missing
            && x.ClassLabel == "U");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(empty.ObservationPairs, Is.Zero);
            Assert.That(empty.KishEquivalentBaseContributorsNotIndependentEss, Is.Null);
            Assert.That(empty.LargestBaseContributionShare, Is.Null);
            Assert.That(empty.DistinctBaseContributors, Is.Zero);
        }));
    }

    [Test]
    public void Rejects_unaligned_SQL_cursor_duplicate_pairs_and_impossible_base_population()
    {
        var mismatch = BirthPairs.ToArray();
        mismatch[0] = mismatch[0] with { BaseB = "UNRELATED" };
        Assert.Throws<InvalidDataException>((Action)(() => Analyze(birth: mismatch)));
        var duplicated = NominalPairs.ToArray();
        duplicated[1] = duplicated[1] with { A = 0, B = 1 };
        Assert.Throws<InvalidDataException>((Action)(() => Analyze(nominal: duplicated)));
        Assert.Throws<InvalidDataException>((Action)(() => Analyze(bases: 2)));
        Assert.Throws<InvalidDataException>((Action)(() => Analyze(bases: 0)));
    }
}
