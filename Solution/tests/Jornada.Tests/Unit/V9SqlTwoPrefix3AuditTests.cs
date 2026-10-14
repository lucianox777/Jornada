using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlTwoPrefix3AuditTests
{
    private static readonly V9SqlTwoPrefix3Audit.Observation[] Rows =
    [
        new(0,"TRAIN","G1",true,"MAR","ANA",new(1975,2,11)),
        new(1,"TRAIN","G2",true,"MAX","ANA",new(1975,2,11)),
        new(2,"TRAIN","G3",true,"MAR","ANA",new(1976,2,11)),
        new(10,"VALIDATION","G1",true,"MAR","ANA",new(1975,2,11)),
        new(11,"VALIDATION","G2",true,"MAX","ANA",new(1975,2,11)),
        new(12,"VALIDATION","G3",true,"MAR","ANA",new(1976,2,11))
    ];
    private static readonly V9SqlNamePrefix3BirthAudit.Pair[] M =
    [
        new(0,1,"TRAIN",true),new(10,11,"VALIDATION",true)
    ];
    private static readonly V9SqlTwoPrefix3Audit.Pass[] Passes =
    [
        new(V9SqlTwoPrefix3Audit.MotherDate,M),
        new(V9SqlTwoPrefix3Audit.PersonMother,[
            new(0,2,"TRAIN",false),new(10,12,"VALIDATION",false)])
    ];
    private static V9SqlNamePrefix3BirthAudit.Split PreviousSplit(string part)
        =>new(part,1,0,0,0,0,0,0,0m,1,0,0,3,3,0,0,0,0,0,0);
    private static V9SqlTwoPrefix3Audit.Report Check(
        IReadOnlyList<V9SqlTwoPrefix3Audit.Observation>? rows=null,
        IReadOnlyList<V9SqlTwoPrefix3Audit.Pass>? passes=null)
        =>V9SqlTwoPrefix3Audit.Analyze(rows??Rows,[],M,passes??Passes,
            new(V9SqlNamePrefix3BirthAudit.Version,V9SqlNamePrefix3BirthAudit.Status,
                V9SqlNamePrefix3BirthAudit.PassId,
                "TRAIN_AND_VALIDATION_ONLY_TEST_FORBIDDEN",false,
                [PreviousSplit("TRAIN"),PreviousSplit("VALIDATION")]),100);

    [Test]
    public void Prefixes_add_M_and_U_with_identical_eligible_denominators()
    {
        var report=Check();
        var train=report.Splits.Single(x=>x.Partition=="TRAIN");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.MayPromotePolicy,Is.False);
            Assert.That(report.Passes,Has.Count.EqualTo(4));
            Assert.That(train.AddedM,Is.EqualTo(1));
            Assert.That(train.AddedU,Is.EqualTo(1));
            Assert.That(train.Recall,Is.EqualTo(1m));
            Assert.That(train.P95UDegree,Is.EqualTo(1));
        }));
    }
    [Test]
    public void Missing_SQL_truth_and_TEST_are_rejected()
    {
        Assert.Throws<InvalidDataException>((Action)(()=>Check(passes:[
            new(V9SqlTwoPrefix3Audit.MotherDate,[]),Passes[1]])));
        Assert.Throws<InvalidDataException>((Action)(()=>Check(rows:[
            ..Rows,new(99,"TEST","G4",true,"MAR","ANA",new(1975,2,11))])));
    }
}
