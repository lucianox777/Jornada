using System.Text.Json;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlClusteredMRecallBootstrapAuditTests
{
    private static V9SqlElevenPassResidualAudit.Report Eleven() =>
        new(V9SqlElevenPassResidualAudit.Version,
            V9SqlElevenPassResidualAudit.Status,
            "TRAIN_AND_VALIDATION_ONLY_TEST_FORBIDDEN",6,5,false,false,
            [
                new V9SqlElevenPassResidualAudit.Split(
                    "TRAIN",5,2,1,3,1,4,1,4,1,0,0,0,0,1,1,
                    4m/5m,8,1,1,2,3,3,1,2,2),
                new V9SqlElevenPassResidualAudit.Split(
                    "VALIDATION",5,2,1,3,1,4,1,4,1,0,0,0,0,1,1,
                    4m/5m,8,1,1,2,3,3,1,2,2)
            ], []);

    private static readonly V9SqlClusteredMRecallBootstrapAudit.Contribution[] Clusters =
    [
        new("TRAIN","synthetic-train-A",3,2,2),
        new("TRAIN","synthetic-train-B",2,0,2),
        new("VALIDATION","synthetic-val-C",2,1,2),
        new("VALIDATION","synthetic-val-D",3,1,2)
    ];

    [Test]
    public void Samples_base_identities_reproducibly_not_pairs_or_TEST()
    {
        var a=V9SqlClusteredMRecallBootstrapAudit.Analyze(
            Clusters,Eleven(),42,"FROZEN_OBSERVATION_FINGERPRINT");
        var b=V9SqlClusteredMRecallBootstrapAudit.Analyze(
            Clusters.Reverse().ToArray(),Eleven(),42,"FROZEN_OBSERVATION_FINGERPRINT");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(a.Splits,Has.Count.EqualTo(2));
            Assert.That(JsonSerializer.Serialize(a),Is.EqualTo(JsonSerializer.Serialize(b)));
            Assert.That(a.IsIndependentPairConfidenceInterval,Is.False);
            Assert.That(a.ModelsNetworkDependenceAmongU,Is.False);
            Assert.That(a.MayPromoteModelOrBlockingPolicy,Is.False);
            Assert.That(a.Splits.All(x=>x.BootstrapReplicates==
                V9SqlClusteredMRecallBootstrapAudit.Replicates),Is.True);
            Assert.That(a.Splits.All(x=>x.ContributingBasePeople==2),Is.True);
            Assert.That(a.Splits.All(x=>x.BaselineRecallObserved==2m/5m),Is.True);
            Assert.That(a.Splits.All(x=>x.ElevenRecallObserved==4m/5m),Is.True);
            Assert.That(a.Splits.All(x=>x.BaselineBootstrapP025<=x.BaselineBootstrapP975
                && x.ElevenBootstrapP025<=x.ElevenBootstrapP975),Is.True);
            Assert.That(a.Splits.All(x=>x.ProbabilityElevenMeets95InBootstrapNotGate is >=0m and <=1m),Is.True);
        }));
        var json=JsonSerializer.Serialize(a);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(json,Does.Not.Contain("synthetic-train-A"));
            Assert.That(json,Does.Not.Contain("synthetic-val-D"));
            Assert.That(json,Does.Not.Contain("SyntheticBaseId"));
            Assert.That(json,Does.Not.Contain("A,"));
        }));
    }

    [Test]
    public void Fails_closed_on_duplicate_base_between_splits_and_bad_denominators()
    {
        var duplicated=Clusters.ToArray();
        duplicated[3]=duplicated[3] with { SyntheticBaseId="synthetic-train-A" };
        Assert.Throws<InvalidDataException>((Action)(()=>
            V9SqlClusteredMRecallBootstrapAudit.Analyze(
                duplicated,Eleven(),42,"fingerprint")));
        var invalid=Clusters.ToArray();
        invalid[0]=invalid[0] with { EligibleM=4 };
        Assert.Throws<InvalidDataException>((Action)(()=>
            V9SqlClusteredMRecallBootstrapAudit.Analyze(
                invalid,Eleven(),42,"fingerprint")));
        Assert.Throws<InvalidDataException>((Action)(()=>
            V9SqlClusteredMRecallBootstrapAudit.Analyze(
                Clusters,Eleven(),42,"")));
        Assert.Throws<InvalidDataException>((Action)(()=>
            V9SqlClusteredMRecallBootstrapAudit.Analyze(
                [..Clusters,new("TEST","test-base",3,2,2)],Eleven(),42,"fp")));
    }
}
