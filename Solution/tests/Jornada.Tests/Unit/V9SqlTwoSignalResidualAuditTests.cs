using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlTwoSignalResidualAuditTests
{
    private static readonly V9SqlTwoSignalResidualAudit.Signal[] Signals =
    [
        new(0,"TRAIN","P","M",2001),
        new(1,"TRAIN","P","M",2001),
        new(2,"TRAIN","Q",null,2001),
        new(3,"TRAIN","P","N",2002),
        new(10,"VALIDATION","A","X",1999),
        new(11,"VALIDATION","B","X",1999),
        new(12,"VALIDATION","C",null,1998)
    ];
    private static readonly V9SqlTwoSignalResidualAudit.Pair[] Truth =
    [
        new(0,1,"TRAIN"),
        new(0,2,"TRAIN"),
        new(0,3,"TRAIN"),
        new(10,11,"VALIDATION"),
        new(10,12,"VALIDATION")
    ];
    private static readonly (int A,int B)[] Actual = [(0,1),(10,11)];

    [Test]
    public void Two_matching_anchors_are_recovered_and_residual_distinguishes_missing()
    {
        var report = V9SqlTwoSignalResidualAudit.Analyze(Signals,Truth,Actual);
        var train=report.Splits.Single(x=>x.Partition=="TRAIN");
        var validation=report.Splits.Single(x=>x.Partition=="VALIDATION");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.Version,Is.EqualTo(V9SqlTwoSignalResidualAudit.Version));
            Assert.That(report.IsGlobalBlockingCeiling,Is.False);
            Assert.That(report.MayPromoteBlockingPolicy,Is.False);
            Assert.That(report.Cells,Has.Count.EqualTo(16));
            Assert.That(report.Scope,Does.Contain("TEST_FORBIDDEN"));
            Assert.That(train.EligibleM,Is.EqualTo(3));
            Assert.That(train.RecoveredElevenM,Is.EqualTo(1));
            Assert.That(train.ResidualM,Is.EqualTo(2));
            Assert.That(train.TwoOrMoreAnchorsEligibleM,Is.EqualTo(1));
            Assert.That(train.TwoOrMoreAnchorsResidualM,Is.Zero);
            Assert.That(train.ResidualWithNoMatchingAnchorM,Is.Zero);
            Assert.That(train.ResidualWithOnlyOneMatchingAnchorM,Is.EqualTo(2));
            Assert.That(train.ResidualWithMissingMotherPhoneticM,Is.EqualTo(1));
            Assert.That(train.ResidualWithDifferentBirthYearM,Is.EqualTo(1));
            Assert.That(validation.EligibleM,Is.EqualTo(2));
            Assert.That(validation.RecoveredElevenM,Is.EqualTo(1));
            Assert.That(validation.ResidualWithNoMatchingAnchorM,Is.EqualTo(1));
            Assert.That(validation.ResidualWithOnlyOneMatchingAnchorM,Is.Zero);
            Assert.That(validation.TwoOrMoreAnchorsResidualM,Is.Zero);
        }));
        var encoded=System.Text.Json.JsonSerializer.Serialize(report);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(encoded,Does.Not.Contain("\\\"PersonPhonetic\\\":"));
            Assert.That(encoded,Does.Not.Contain("\\\"MotherPhonetic\\\":"));
            Assert.That(encoded,Does.Not.Contain("\\\"BirthYear\\\":"));
            Assert.That(encoded,Does.Not.Contain("\\\"Id\\\":"));
        }));
    }

    [Test]
    public void Rejects_missing_physical_SQL_candidate_with_two_real_agreements()
    {
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlTwoSignalResidualAudit.Analyze(Signals,Truth,[(10,11)])));
    }

    [Test]
    public void Rejects_TEST_duplicate_and_false_retrieved_identity()
    {
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlTwoSignalResidualAudit.Analyze(
                [..Signals,new(99,"TEST","X","Y",1998)],Truth,Actual)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlTwoSignalResidualAudit.Analyze(
                [..Signals,Signals[0]],Truth,Actual)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlTwoSignalResidualAudit.Analyze(Signals,
                [..Truth,Truth[0]],Actual)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlTwoSignalResidualAudit.Analyze(Signals,Truth,
                [..Actual,(2,3)])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlTwoSignalResidualAudit.Analyze(Signals,Truth,
                [..Actual,(0,1)])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlTwoSignalResidualAudit.Analyze(Signals,Truth,
                [..Actual,(2,0)])));
    }
}
