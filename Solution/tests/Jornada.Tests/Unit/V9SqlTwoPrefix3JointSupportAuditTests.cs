using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlTwoPrefix3JointSupportAuditTests
{
    private static V9SqlTwoPrefix3Probe.Observation Row(
        int id,string baseId,string partition,string gestor,
        string person,string mother,int year) =>
        new(id,baseId,partition,gestor,person,mother,
            new DateOnly(year,2,11),null);

    private static readonly V9SqlTwoPrefix3Probe.Observation[] Rows =
    [
        Row(0,"A","TRAIN","G1","MAR","ANA",1975),
        Row(1,"A","TRAIN","G2","MAX","ANA",1975),
        Row(2,"B","TRAIN","G3","MAR","ANA",1976),
        Row(10,"C","VALIDATION","G1","MAR","ANA",1975),
        Row(11,"C","VALIDATION","G2","MAX","ANA",1975),
        Row(12,"D","VALIDATION","G3","MAR","ANA",1976)
    ];

    private static readonly V9SqlTwoPrefix3Audit.Pass[] Passes =
    [
        new(V9SqlTwoPrefix3Audit.MotherDate,
        [
            new(0,1,"TRAIN",true),new(10,11,"VALIDATION",true)
        ]),
        new(V9SqlTwoPrefix3Audit.PersonMother,
        [
            new(0,2,"TRAIN",false),new(10,12,"VALIDATION",false)
        ])
    ];

    private static V9SqlTwoPrefix3Audit.Report Physical() =>
        new(V9SqlTwoPrefix3Audit.Version,
            "DIAGNOSTIC_ONLY_TRAIN_VALIDATION_NO_TEST",false,
            [
                new(V9SqlTwoPrefix3Audit.MotherDate,"TRAIN",1,0,1,0,1,0),
                new(V9SqlTwoPrefix3Audit.MotherDate,"VALIDATION",1,0,1,0,1,0),
                new(V9SqlTwoPrefix3Audit.PersonMother,"TRAIN",0,1,0,1,0,0),
                new(V9SqlTwoPrefix3Audit.PersonMother,"VALIDATION",0,1,0,1,0,0)
            ],
            [
                new("TRAIN",1,0,0,1,1,1,1,1m,0,3,0,2,2,2,1,1,1),
                new("VALIDATION",1,0,0,1,1,1,1,1m,0,3,0,2,2,2,1,1,1)
            ]);

    [Test]
    public void Joint_base_key_support_is_observed_not_product_of_marginals()
    {
        var report=V9SqlTwoPrefix3JointSupportAudit.Analyze(Rows,Passes,Physical(),100);
        var joint=report.Keys.Single(x=>x.Partition=="TRAIN"
            && x.Pass==V9SqlTwoPrefix3Audit.PersonMother);
        Assert.Multiple((Action)(()=>
        {
            Assert.That(report.MayPromotePolicy,Is.False);
            Assert.That(report.IsObservedPopulationJointDistribution,Is.False);
            Assert.That(joint.DistinctBases,Is.EqualTo(2));
            Assert.That(joint.DistinctJointKeys,Is.EqualTo(2));
            Assert.That(joint.DistinctBaseKeyOccurrences,Is.EqualTo(3));
            Assert.That(joint.MaxBasesPerJointKey,Is.EqualTo(2));
        }));
    }

    [Test]
    public void Per_pass_hypothetical_fdr_is_not_FS_fdr_and_includes_zero_degree()
    {
        var report=V9SqlTwoPrefix3JointSupportAudit.Analyze(Rows,Passes,Physical(),100);
        var mother=report.Passes.Single(x=>x.Partition=="TRAIN"
            && x.Pass==V9SqlTwoPrefix3Audit.MotherDate);
        var person=report.Passes.Single(x=>x.Partition=="TRAIN"
            && x.Pass==V9SqlTwoPrefix3Audit.PersonMother);
        Assert.Multiple((Action)(()=>
        {
            Assert.That(mother.M,Is.EqualTo(1));
            Assert.That(mother.HypotheticalFdrIfAllAccepted,Is.EqualTo(0m));
            Assert.That(person.U,Is.EqualTo(1));
            Assert.That(person.HypotheticalFdrIfAllAccepted,Is.EqualTo(1m));
            Assert.That(person.UPerM,Is.Null);
            Assert.That(person.ZeroCandidateObservations,Is.EqualTo(1));
            Assert.That(person.P99UDegree,Is.EqualTo(1));
        }));
    }

    [Test]
    public void Test_partition_and_incorrect_sql_predicate_fail_closed()
    {
        Assert.Throws<InvalidDataException>((Action)(()=>
            V9SqlTwoPrefix3JointSupportAudit.Analyze(
                [..Rows,Row(99,"Z","TEST","G4","MAR","ANA",1975)],
                Passes,Physical(),100)));
        var wrong=new V9SqlTwoPrefix3Audit.Pass[]
        {
            new(V9SqlTwoPrefix3Audit.MotherDate,
                [new(0,2,"TRAIN",false)]),
            Passes[1]
        };
        Assert.Throws<InvalidDataException>((Action)(()=>
            V9SqlTwoPrefix3JointSupportAudit.Analyze(
                Rows,wrong,Physical(),100)));
    }
}
