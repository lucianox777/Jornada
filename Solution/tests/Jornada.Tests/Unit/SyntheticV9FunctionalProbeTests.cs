using Jornada.Linkage.SyntheticCorpus;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SyntheticV9FunctionalProbeTests
{
    private static (SyntheticPerson[] People, SyntheticObservation[] Observations) Corpus()
    {
        var people=new List<SyntheticPerson>();
        var observations=new List<SyntheticObservation>();
        foreach(var split in new[]{"TRAIN","VALIDATION","TEST"})
            for(var i=0;i<135;i++)
            {
                var id=$"PRIVATE-{split}-{i:D4}";
                var birth=new DateOnly(1990,1,1); // deliberately indistinguishable M and U
                people.Add(new SyntheticPerson
                {
                    BasePersonId=id,Partition=split,Name="Maria Silva",
                    MotherName="Ana Costa",BirthDate=birth,Sex="F",
                    EvaluationWeight=1d
                });
                foreach(var gestor in new[]{"G1","G2"})
                    observations.Add(new SyntheticObservation
                    {
                        ObservationId=id+"-"+gestor,BasePersonId=id,
                        Partition=split,Gestor=gestor,Name="Maria Silva",
                        MotherName="Ana Costa",BirthDate=birth,Sex="F",
                        Corruptions="",EvaluationWeight=1d
                    });
            }
        return (people.ToArray(),observations.ToArray());
    }

    [Test]
    public void A_real_FS_V9_probe_emits_only_aggregate_truth_metrics_and_can_fail()
    {
        var (people,observations)=Corpus();
        var report=SyntheticV9FunctionalProbe.Analyze(people,observations,42,"FIXTURE");
        var json=System.Text.Json.JsonSerializer.Serialize(report);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.People,Is.EqualTo(405));
            Assert.That(report.TrainMatchedRetrieved,Is.EqualTo(135));
            Assert.That(report.TrainUnmatchedSampled,Is.EqualTo(134));
            Assert.That(report.Test.Tp+report.Test.Fn,Is.EqualTo(135));
            Assert.That(report.Test.Fp+report.Test.Tn,Is.EqualTo(134));
            Assert.That(report.Validation.Fpr,Is.LessThanOrEqualTo(report.ValidationFprBudget));
            Assert.That(report.TrainCellsWithoutBilateralSupport,Is.GreaterThan(0));
            Assert.That(report.SyntheticProxyVerdict,Is.EqualTo("FAIL_ON_SAMPLED_SYNTHETIC_PROXY"));
            Assert.That(report.OperationalConclusion,Does.Contain("NOT_PROVEN"));
            Assert.That(json,Does.Not.Contain("PRIVATE-"));
            Assert.That(json,Does.Not.Contain("Maria Silva"));
            Assert.That(json,Does.Not.Contain("Ana Costa"));
        }));
    }

    [Test]
    public void Frozen_seed42_model_is_reused_unchanged_across_seeds()
    {
        var (people, observations) = Corpus();
        var source = SyntheticV9FunctionalProbe.Analyze(people, observations, 42, "SOURCE42");
        var frozen = source.FrozenModel!;
        var target = SyntheticV9FunctionalProbe.Analyze(people, observations, 43, "TARGET43", frozen);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(source.CalibrationMode, Is.EqualTo(SyntheticV9FunctionalProbe.FitLocally));
            Assert.That(target.CalibrationMode, Is.EqualTo(SyntheticV9FunctionalProbe.Transferred));
            Assert.That(source.FrozenModelSha256, Is.EqualTo(target.FrozenModelSha256));
            Assert.That(source.FrozenThresholdLogOdds, Is.EqualTo(target.FrozenThresholdLogOdds));
            Assert.That(target.FrozenModel!.SourceSeed, Is.EqualTo(42UL));
            Assert.That(target.FrozenModel.SourceInputFingerprint, Is.EqualTo("SOURCE42"));
        }));
        Assert.That((Action)(() => SyntheticV9FunctionalProbe.Analyze(
            people, observations, 43, "TARGET43")), Throws.InvalidOperationException);
        Assert.That((Action)(() => SyntheticV9FunctionalProbe.Analyze(
            people, observations, 42, "SOURCE42", frozen)), Throws.InvalidOperationException);
        Assert.That((Action)(() => SyntheticV9FunctionalProbe.Analyze(
            people, observations, 43, "TARGET43", frozen with { Cells = frozen.Cells[..80] })),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Rejects_cross_partition_base_and_duplicate_truth()
    {
        var (people,observations)=Corpus();
        var source=observations[0];
        var invalid=observations.ToArray();
        invalid[0]=new SyntheticObservation
        {
            ObservationId=source.ObservationId,BasePersonId=source.BasePersonId,
            Partition="TEST",Gestor=source.Gestor,Name=source.Name,
            MotherName=source.MotherName,BirthDate=source.BirthDate,
            Sex=source.Sex,Corruptions=source.Corruptions,
            EvaluationWeight=source.EvaluationWeight
        };
        Assert.That((Action)(()=>SyntheticV9FunctionalProbe.Analyze(
            people,invalid,42,"FIXTURE")),Throws.InvalidOperationException);
        Assert.That((Action)(()=>SyntheticV9FunctionalProbe.Analyze(
            people.Concat([people[0]]).ToArray(),observations,42,"FIXTURE")),
            Throws.InvalidOperationException);
    }
}
