using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlBirthYearPassFamilyAuditTests
{
    [Test]
    public void Contract_remains_non_operational()
    {
        Assert.That(V9SqlBirthYearPassFamilyAudit.Version, Does.Contain("TRAIN_VALIDATION"));
        Assert.That(V9SqlBirthYearPassFamilyAudit.RegisteredPasses, Has.Count.EqualTo(5));
    }

    private static readonly V9SqlBirthYearPassFamilyAudit.Observation[] Rows =
    [
        new(0, "TRAIN", "G1", true), new(1, "TRAIN", "G2", true),
        new(2, "TRAIN", "G3", true), new(3, "TRAIN", "G1", true),
        new(4, "TRAIN", "G4", true),
        new(5, "VALIDATION", "G1", true), new(6, "VALIDATION", "G2", true),
        new(7, "VALIDATION", "G3", true), new(8, "VALIDATION", "G1", true),
        new(9, "VALIDATION", "G4", true),
        new(10, "TRAIN", "G5", false)
    ];
    private static readonly V9SqlBirthYearPassFamilyAudit.Pair[] Baseline =
    [
        new(0, 1, "TRAIN", true), new(1, 3, "TRAIN", false),
        new(5, 6, "VALIDATION", true), new(6, 8, "VALIDATION", false)
    ];
    private static readonly V9SqlBirthYearPassFamilyAudit.Pass[] Passes =
    [
        new("PERSON_EXACT_PLUS_BIRTH_YEAR", [
            new(0, 2, "TRAIN", true), new(5, 7, "VALIDATION", true)]),
        new("PERSON_PHONETIC_PLUS_BIRTH_YEAR", [
            new(0, 2, "TRAIN", true), new(5, 7, "VALIDATION", true),
            new(2, 3, "TRAIN", false)]),
        new("MOTHER_EXACT_PLUS_BIRTH_YEAR", [
            new(2, 3, "TRAIN", false), new(7, 8, "VALIDATION", false)]),
        new("MOTHER_PHONETIC_PLUS_BIRTH_YEAR", [
            new(2, 3, "TRAIN", false), new(5, 7, "VALIDATION", true)]),
        new("PERSON_AND_MOTHER_PHONETIC_ANY_BIRTH", [
            new(0, 2, "TRAIN", true), new(7, 8, "VALIDATION", false)])
    ];

    private static V9SqlBirthYearPassFamilyAudit.Report Run(
        IReadOnlyList<V9SqlBirthYearPassFamilyAudit.Observation>? rows = null,
        IReadOnlyList<V9SqlBirthYearPassFamilyAudit.Pass>? passes = null,
        IReadOnlyList<V9SqlBirthYearPassFamilyAudit.Pair>? baseline = null,
        int cap = 1000) =>
        V9SqlBirthYearPassFamilyAudit.Analyze(
            rows ?? Rows, baseline ?? Baseline, passes ?? Passes,
            2, 2, cap);

    [Test]
    public void All_five_ceiling_deduplicates_M_U_and_includes_zero_candidates()
    {
        var report = Run();
        var train = report.AllFiveUnionCeiling.Single(x => x.Partition == "TRAIN");
        var val = report.AllFiveUnionCeiling.Single(x => x.Partition == "VALIDATION");
        var one = report.Splits.Single(x =>
            x.Partition == "TRAIN" && x.PassId == "PERSON_PHONETIC_PLUS_BIRTH_YEAR");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.Splits, Has.Count.EqualTo(10));
            Assert.That(report.AllFiveUnionCeiling, Has.Count.EqualTo(2));
            Assert.That(report.CanSelectOrPromotePolicy, Is.False);
            Assert.That(train.BaselineM, Is.EqualTo(1));
            Assert.That(train.BaselineU, Is.EqualTo(1));
            Assert.That(train.ExtraM, Is.EqualTo(1));
            Assert.That(train.ExtraU, Is.EqualTo(1));
            Assert.That(train.UnionM, Is.EqualTo(2));
            Assert.That(train.UnionU, Is.EqualTo(2));
            Assert.That(train.ObservationsWithoutCandidates, Is.EqualTo(1));
            Assert.That(train.EligibleObservations, Is.EqualTo(5));
            Assert.That(train.P95TotalCandidates, Is.EqualTo(2));
            Assert.That(train.P99FalseCandidates, Is.EqualTo(2));
            Assert.That(train.TotalDegreeHistogram.Sum(
                x => x.Degree * x.Observations), Is.EqualTo(8));
            Assert.That(train.FalseDegreeHistogram.Sum(
                x => x.Degree * x.Observations), Is.EqualTo(4));
            Assert.That(val.UnionM, Is.EqualTo(2));
            Assert.That(val.ObservationsWithoutCandidates, Is.EqualTo(1));
            Assert.That(one.ExtraM, Is.EqualTo(1));
            Assert.That(one.ExtraU, Is.EqualTo(1));
        }));
        var json = System.Text.Json.JsonSerializer.Serialize(report);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(json, Does.Not.Contain("Gestor"));
            Assert.That(json, Does.Not.Contain("BasePersonId"));
            Assert.That(json, Does.Not.Contain("BirthDate"));
        }));
    }

    [Test]
    public void Test_leakage_unknown_endpoint_conflicting_labels_and_cap_fail_closed()
    {
        Assert.Throws<InvalidDataException>((Action)(() =>
            Run(rows: [.. Rows, new(99, "TEST", "G1", true)])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            Run(rows: Rows.Select(x => x.Id == 0
                ? x with { Eligible = false } : x).ToArray())));
        var test = Passes.ToArray();
        test[0] = test[0] with { ExtraPairs = [new(99, 100, "TEST", true)] };
        Assert.Throws<InvalidDataException>((Action)(() => Run(passes: test)));
        var conflict = Passes.ToArray();
        conflict[1] = conflict[1] with {
            ExtraPairs = [new(0, 2, "TRAIN", false)] };
        Assert.Throws<InvalidDataException>((Action)(() => Run(passes: conflict)));
        Assert.Throws<InvalidDataException>((Action)(() => Run(cap: 7)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            Run(baseline: [.. Baseline, Baseline[0]])));
    }
}
