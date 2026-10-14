using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9NameRequiredSqlCellAuditTests
{
    private static readonly V9NameRequiredSqlCellAudit.Observation[] Rows =
    [
        new(0,"A","TRAIN","G1",null,"MARIA SILVA","ANA DE SOUZA",new(1980,1,1)),
        new(1,"A","TRAIN","G2",null,"MARIA SILVA","ANA DE SOUZA",new(1981,1,1)),
        new(2,"B","TRAIN","G1",null,"JOAO SILVA",null,new(1980,2,1)),
        new(3,"B","TRAIN","G2",null,"JOAO SILVA",null,new(1980,3,1)),
        new(4,"C","TRAIN","G1",null,"MARIA SILVA","ANA DE SOUZA",new(1980,1,1)),
        new(5,"D","TRAIN","G2",null,"MARIA SILVA","ANA DE SOUZA",new(1980,1,1)),
        new(6,"E","TRAIN","G1",null,"MARIA SILVA","ANA SILVA OLIVEIRA",null),
        new(7,"E","TRAIN","G2",null,"MARIA SILVA","ANA OLIVEIRA",null),
        new(20,"X","VALIDATION","G1",null,"PAULO SOUZA","ANA DE SOUZA",new(1970,1,1)),
        new(21,"X","VALIDATION","G2",null,"PAULO SOUZA","ANA DE SOUZA",new(1970,1,1)),
        new(22,"Y","VALIDATION","G3",null,"SOFIA SOUZA",null,null)
    ];
    private static readonly V9SqlNamePrefix3BirthAudit.Pair[] Candidates =
    [
        new(0,1,"TRAIN",true),new(2,3,"TRAIN",true),
        new(4,5,"TRAIN",false),new(6,7,"TRAIN",true),
        new(20,21,"VALIDATION",true)
    ];
    private static IReadOnlyList<V9NameRequiredSqlCellAudit.Pass> Passes()
        => V9NameRequiredSqlCellAudit.Order.Select(id =>
            new V9NameRequiredSqlCellAudit.Pass(id,Candidates
                .Where(x => V9NameRequiredSqlCellAudit.Matches(id,Rows.Single(y=>y.Id==x.A),
                    Rows.Single(y=>y.Id==x.B))).ToArray())).ToArray();

    [Test]
    public void Surname_presence_covers_any_position_but_prioritizes_final_content()
    {
        var all=V9NameRequiredSqlCellAudit.AnySurname("Maria da Silva Oliveira");
        Assert.That(all,Is.EquivalentTo(new[]{"SILVA","OLIVEIRA"}));
        Assert.That(V9NameRequiredSqlCellAudit.Last("Maria da Silva Oliveira"),Is.EqualTo("OLIVEIRA"));
        Assert.That(V9NameRequiredSqlCellAudit.Last("Joao Silva Filho"),Is.EqualTo("SILVA"));
        Assert.That(V9NameRequiredSqlCellAudit.AnySurname("Joao Silva Filho"),Does.Not.Contain("FILHO"));
        Assert.That(V9NameRequiredSqlCellAudit.AnySurname("MARIA"),Is.Empty);
        Assert.That(V9NameRequiredSqlCellAudit.AnySurname("ANA DE SOUZA"),Is.EquivalentTo(new[]{"SOUZA"}));
    }

    [Test]
    public void Counts_unions_and_full_truth_including_pm_without_dob()
    {
        var old=new V9SqlNamePrefix3BirthAudit.Pair[]
        {
            new(0,1,"TRAIN",true),new(20,21,"VALIDATION",true)
        };
        var report=V9NameRequiredSqlCellAudit.Analyze(Rows,old,Passes(),100);
        var train=report.Splits.Single(x=>x.Partition=="TRAIN");
        Assert.That(report.MayPromotePolicy,Is.False);
        Assert.That(report.FdrMeasured,Is.False);
        Assert.That(report.Cells,Has.Count.EqualTo(8));
        Assert.That(train.EligibleTruthM,Is.EqualTo(3));
        Assert.That(train.LegacyM,Is.EqualTo(1));
        Assert.That(train.NewM,Is.EqualTo(2));
        Assert.That(train.UnionM,Is.EqualTo(3));
        Assert.That(train.MissingBirthTruthM,Is.EqualTo(1));
        Assert.That(train.UnionRecall,Is.EqualTo(1m));
        Assert.That(train.MoreMNeededFor95,Is.EqualTo(0));
        Assert.That(train.UnionU,Is.EqualTo(1));
        Assert.That(report.Cells.Any(x=>x.Partition=="TRAIN" && x.Name==
            V9NameRequiredSqlCellAudit.PersonPhoneticMotherAny && x.M>0),Is.True);
        Assert.That(report.Splits.Single(x=>x.Partition=="VALIDATION").UnionRecall,Is.EqualTo(1m));
    }

    [Test]
    public void Rejects_Test_wrong_label_duplicate_edge_and_overflow()
    {
        Assert.Throws<InvalidDataException>((Action)(()=>V9NameRequiredSqlCellAudit.Analyze(
            [..Rows,new(90,"Z","TEST","G9",null,"MARIA","ANA",null)],
            [],Passes(),100)));
        var passes=Passes().ToArray();
        passes[0]=passes[0] with {Pairs=[new(0,1,"TRAIN",false)]};
        Assert.Throws<InvalidDataException>((Action)(()=>V9NameRequiredSqlCellAudit.Analyze(
            Rows,[],passes,100)));
        Assert.Throws<InvalidDataException>((Action)(()=>V9NameRequiredSqlCellAudit.Analyze(
            Rows,[],Passes(),1)));
    }
}
