using System.Text.Json;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlElevenPassResidualAuditTests
{
    private static readonly V9SqlBirthYearPassFamilyAudit.Observation[] Rows =
    [
        new(0,"TRAIN","G1",true),new(1,"TRAIN","G2",true),
        new(2,"TRAIN","G3",true),new(3,"TRAIN","G4",true),
        new(4,"TRAIN","G5",true), // eligible but zero-hit
        new(10,"VALIDATION","G1",true),new(11,"VALIDATION","G2",true),
        new(12,"VALIDATION","G3",true),new(13,"VALIDATION","G4",true),
        new(14,"VALIDATION","G5",true)
    ];
    private static readonly V9SqlTrainValidationUnionSearch.Pair[] Baseline =
    [
        new(0,1,"TRAIN",true),new(1,2,"TRAIN",false),
        new(10,11,"VALIDATION",true),new(11,12,"VALIDATION",false)
    ];
    private static readonly V9SqlTrainValidationUnionSearch.Extension[] Old =
    [
        new("A",[new(0,2,"TRAIN",true),new(10,12,"VALIDATION",true),
            new(2,3,"TRAIN",false)]),
        new("B",[]),new("C",[]),new("D",[]),new("E",[]),new("F",[])
    ];
    private static readonly V9SqlBirthYearPassFamilyAudit.Pass[] New =
    [
        new("PERSON_EXACT_PLUS_BIRTH_YEAR", [
            new(0,3,"TRAIN",true),new(10,13,"VALIDATION",true)]),
        new("PERSON_PHONETIC_PLUS_BIRTH_YEAR",[]),
        new("MOTHER_EXACT_PLUS_BIRTH_YEAR",[]),
        new("MOTHER_PHONETIC_PLUS_BIRTH_YEAR",[
            new(2,3,"TRAIN",false),new(12,13,"VALIDATION",false)]),
        new("PERSON_AND_MOTHER_PHONETIC_ANY_BIRTH",[])
    ];
    private static readonly V9BlockingTruthLossAudit.Pair[] Truth =
    [
        new(0,1,"TRAIN",false,true,false,true,true),
        new(0,2,"TRAIN",false,false,true,false,true),
        new(0,3,"TRAIN",false,false,false,false,false),
        new(10,11,"VALIDATION",false,true,false,true,true),
        new(10,12,"VALIDATION",false,false,true,false,true),
        new(10,13,"VALIDATION",false,false,false,false,false)
    ];

    private static V9SqlElevenPassResidualAudit.Report Run(
        IReadOnlyList<V9SqlBirthYearPassFamilyAudit.Observation>? rows = null,
        IReadOnlyList<V9SqlTrainValidationUnionSearch.Pair>? baseline = null,
        IReadOnlyList<V9SqlTrainValidationUnionSearch.Extension>? oldPasses = null,
        IReadOnlyList<V9SqlBirthYearPassFamilyAudit.Pass>? newPasses = null,
        IReadOnlyList<V9BlockingTruthLossAudit.Pair>? truth = null, int cap = 1000)
    {
        var b=baseline ?? Baseline;
        var six=oldPasses ?? Old;
        var five=newPasses ?? New;
        var oldSearch=V9SqlTrainValidationUnionSearch.Analyze(b,six,3,3,cap);
        var newSearch=V9SqlBirthYearPassFamilyAudit.Analyze(
            rows ?? Rows,
            b.Select(x=>new V9SqlBirthYearPassFamilyAudit.Pair(
                x.A,x.B,x.Partition,x.IsReferenceMatch)).ToArray(),
            five,3,3,cap);
        return V9SqlElevenPassResidualAudit.Analyze(
            rows ?? Rows,b,six,five,truth ?? Truth,oldSearch,newSearch,cap);
    }

    [Test]
    public void Reconciles_distinct_six_plus_five_SQL_unions_and_36_residual_M_cells()
    {
        var result=Run();
        var train=result.Splits.Single(x=>x.Partition=="TRAIN");
        var validation=result.Splits.Single(x=>x.Partition=="VALIDATION");
        var remaining=result.ResidualCells.Single(x=>x.Partition=="TRAIN"
            && x.NameState=="DIFFERENT" && x.MotherState=="DIFFERENT"
            && x.BirthState=="DIFFERENT");
        Assert.Multiple((Action)(()=>
        {
            Assert.That(result.Version,Is.EqualTo(V9SqlElevenPassResidualAudit.Version));
            Assert.That(result.OldRegisteredPassCount,Is.EqualTo(6));
            Assert.That(result.NewRegisteredPassCount,Is.EqualTo(5));
            Assert.That(result.Passes95PercentInBothSplits,Is.True);
            Assert.That(result.MayPromoteBlockingPolicy,Is.False);
            Assert.That(result.ResidualCells,Has.Count.EqualTo(36));
            Assert.That(train.EligibleM,Is.EqualTo(3));
            Assert.That(train.BaselineM,Is.EqualTo(1));
            Assert.That(train.BaselineU,Is.EqualTo(1));
            Assert.That(train.SixUnionM,Is.EqualTo(2));
            Assert.That(train.FiveUnionM,Is.EqualTo(2));
            Assert.That(train.ElevenUnionM,Is.EqualTo(3));
            Assert.That(train.ElevenUnionU,Is.EqualTo(2));
            Assert.That(train.RecoveredOnlyBySixM,Is.EqualTo(1));
            Assert.That(train.RecoveredOnlyByFiveM,Is.EqualTo(1));
            Assert.That(train.RecoveredOnlyBySixU,Is.Zero); // shared U
            Assert.That(train.RecoveredOnlyByFiveU,Is.Zero);
            Assert.That(train.ResidualUnretrievedM,Is.Zero);
            Assert.That(train.AdditionalMNeededFor95Percent,Is.Zero);
            Assert.That(train.EligibleObservations,Is.EqualTo(5));
            Assert.That(train.ObservationsWithoutCandidates,Is.EqualTo(1));
            Assert.That(train.P95Candidates,Is.EqualTo(3));
            Assert.That(train.P99FalseCandidates,Is.EqualTo(2));
            Assert.That(validation.ElevenUnionM,Is.EqualTo(3));
            Assert.That(validation.ElevenUnionU,Is.EqualTo(2));
            Assert.That(remaining.EligibleM,Is.EqualTo(1));
            Assert.That(remaining.RecoveredBaselineM,Is.Zero);
            Assert.That(remaining.RecoveredElevenM,Is.EqualTo(1));
            Assert.That(remaining.ResidualUnretrievedM,Is.Zero);
        }));
        var json=JsonSerializer.Serialize(result);
        Assert.Multiple((Action)(()=>
        {
            Assert.That(json,Does.Not.Contain("Gestor"));
            Assert.That(json,Does.Not.Contain("LowerObservationId"));
            Assert.That(json,Does.Not.Contain("BasePersonId"));
        }));
    }

    [Test]
    public void Rejects_wrong_M_ground_truth_inconsistent_labels_and_TEST_frame()
    {
        var invalidTruth=Truth.ToArray();
        invalidTruth[0]=invalidTruth[0] with { HigherObservationId=4 };
        Assert.Throws<InvalidDataException>((Action)(()=>Run(truth:invalidTruth)));
        var incompatible=New.ToArray();
        incompatible[0]=incompatible[0] with {
            ExtraPairs=[new(0,2,"TRAIN",false)] };
        Assert.Throws<InvalidDataException>((Action)(()=>Run(newPasses:incompatible)));
        Assert.Throws<InvalidDataException>((Action)(()=>Run(
            rows:[.. Rows,new(99,"TEST","G1",true)])));
        Assert.Throws<InvalidDataException>((Action)(()=>Run(cap:4)));
    }
}
