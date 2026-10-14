using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlNamePrefix3BirthAuditTests
{
    private static V9SqlElevenPassResidualAudit.Split Expected(
        string part,long eligible,long m,long u) =>
        new(part,eligible,0,0,0,0,0,0,m,u,
            0,0,0,0,0,0,0m,0,0,0,0,0,0,0,0,0);
    private static readonly V9SqlNamePrefix3BirthAudit.Observation[] Rows =
    [
        new(0,"TRAIN","G1",true,"MAR",new(1975,2,11)),
        new(1,"TRAIN","G2",true,"MAR",new(1975,2,11)),
        new(2,"TRAIN","G3",true,"MAR",new(1975,2,11)),
        new(3,"TRAIN","G1",true,"JOS",new(1980,1,1)),
        new(4,"TRAIN","G2",true,"JOS",new(1980,1,1)),
        new(10,"VALIDATION","G1",true,"MAR",new(1975,2,11)),
        new(11,"VALIDATION","G2",true,"MAR",new(1975,2,11)),
        new(12,"VALIDATION","G3",true,"MAR",new(1975,2,11)),
        new(13,"VALIDATION","G1",true,"JOS",new(1980,1,1)),
        new(14,"VALIDATION","G2",true,"JOS",new(1980,1,1))
    ];
    private static readonly V9SqlNamePrefix3BirthAudit.Pair[] Eleven =
    [
        new(3,4,"TRAIN",true),new(13,14,"VALIDATION",true)
    ];
    private static readonly V9SqlNamePrefix3BirthAudit.Pair[] Prefix =
    [
        new(0,1,"TRAIN",true),
        new(0,2,"TRAIN",false),new(1,2,"TRAIN",false),
        new(3,4,"TRAIN",true),
        new(10,11,"VALIDATION",true),
        new(10,12,"VALIDATION",false),new(11,12,"VALIDATION",false),
        new(13,14,"VALIDATION",true)
    ];
    private static readonly V9SqlNamePrefix3BirthAudit.Pair[] Truth =
    [
        new(0,1,"TRAIN",true),new(3,4,"TRAIN",true),
        new(10,11,"VALIDATION",true),new(13,14,"VALIDATION",true)
    ];
    private static readonly V9SqlElevenPassResidualAudit.Split[] Expectations =
    [
        Expected("TRAIN",2,1,0),Expected("VALIDATION",2,1,0)
    ];
    private static V9SqlNamePrefix3BirthAudit.Report Run(
        IReadOnlyList<V9SqlNamePrefix3BirthAudit.Observation>? observations=null,
        IReadOnlyCollection<V9SqlNamePrefix3BirthAudit.Pair>? prefix=null,
        IReadOnlyCollection<V9SqlNamePrefix3BirthAudit.Pair>? eleven=null,
        int cap=100) => V9SqlNamePrefix3BirthAudit.Analyze(
            observations??Rows,eleven??Eleven,prefix??Prefix,Truth,Expectations,cap);

    [Test]
    public void SQL_prefix_three_and_exact_birth_adds_M_U_and_deduplicates_against_eleven()
    {
        var report=Run();
        var train=report.Splits.Single(x=>x.Partition=="TRAIN");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.PassId,Is.EqualTo("PERSON_NAME_PREFIX3_AND_EXACT_BIRTH"));
            Assert.That(report.Splits,Has.Count.EqualTo(2));
            Assert.That(report.MaySelectOrPromotePolicy,Is.False);
            Assert.That(train.EligibleM,Is.EqualTo(2));
            Assert.That(train.ElevenM,Is.EqualTo(1));
            Assert.That(train.ExtraM,Is.EqualTo(1));
            Assert.That(train.ExtraU,Is.EqualTo(2));
            Assert.That(train.TwelveM,Is.EqualTo(2));
            Assert.That(train.TwelveU,Is.EqualTo(2));
            Assert.That(train.TwelveRecall,Is.EqualTo(1m));
            Assert.That(train.EligibleTrueMWithPrefix3ExactBirth,Is.EqualTo(2));
            Assert.That(train.Prefix3TruthMAbsentAfterSql,Is.Zero);
            Assert.That(train.P95Candidates,Is.EqualTo(2));
            Assert.That(train.P99FalseCandidates,Is.EqualTo(2));
            Assert.That(train.RequiredAdditionalMFor95Percent,Is.Zero);
        }));
        var json=System.Text.Json.JsonSerializer.Serialize(report);
        Assert.That(json,Does.Not.Contain("\"Prefix3\":"));
        Assert.That(json,Does.Not.Contain("\"Birth\":"));
    }

    [Test]
    public void Missing_SQL_prefix_M_and_TEST_leakage_are_fail_closed()
    {
        Assert.Throws<InvalidDataException>((Action)(()=>
            Run(prefix:Prefix.Where(x=>x.A!=0||x.B!=1).ToArray())));
        Assert.Throws<InvalidDataException>((Action)(()=>
            Run(observations:[..Rows,new(99,"TEST","G1",true,"MAR",new(1975,2,11))])));
        Assert.Throws<InvalidDataException>((Action)(()=>
            Run(prefix:[..Prefix,new(0,1,"TRAIN",true)])));
        Assert.Throws<InvalidDataException>((Action)(()=>
            Run(prefix:Prefix.Select(x=>x.A==0&&x.B==2?x with {Match=true}:x).ToArray())));
        Assert.Throws<InvalidDataException>((Action)(()=>Run(cap:1)));
    }
}
