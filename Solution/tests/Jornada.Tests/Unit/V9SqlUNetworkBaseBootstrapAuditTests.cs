using System.Text.Json;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlUNetworkBaseBootstrapAuditTests
{
    private static readonly V9SqlUNetworkBaseBootstrapAudit.Base[] Bases =
    [
        new("TRAIN","t-A"),new("TRAIN","t-B"),
        new("TRAIN","t-C"),new("TRAIN","t-D"),
        new("VALIDATION","v-A"),new("VALIDATION","v-B"),
        new("VALIDATION","v-C"),new("VALIDATION","v-D")
    ];
    private static readonly V9SqlUNetworkBaseBootstrapAudit.Edge[] UEdges =
    [
        new("TRAIN","t-A","t-B"),
        new("TRAIN","t-A","t-B"), // two distinct SQL obs pairs: same base edge
        new("TRAIN","t-A","t-C"),
        new("VALIDATION","v-A","v-B"),
        new("VALIDATION","v-B","v-C")
    ];
    private static readonly V9SqlUNetworkBaseBootstrapAudit.Expected[] Observed =
    [
        new("TRAIN",3),new("VALIDATION",2)
    ];
    private static V9SqlUNetworkBaseBootstrapAudit.Report Analyze(
        IReadOnlyList<V9SqlUNetworkBaseBootstrapAudit.Base>? bases=null,
        IReadOnlyList<V9SqlUNetworkBaseBootstrapAudit.Edge>? edges=null,
        IReadOnlyList<V9SqlUNetworkBaseBootstrapAudit.Expected>? expected=null) =>
        V9SqlUNetworkBaseBootstrapAudit.Analyze(
            bases ?? Bases,edges ?? UEdges,expected ?? Observed,42,"CORPUS_FROZEN_FINGERPRINT");

    [Test]
    public void Bootstrap_two_endpoints_preserves_multi_edges_and_zero_degree_bases()
    {
        var report=Analyze();
        var train=report.Splits.Single(x=>x.Partition=="TRAIN");
        var validation=report.Splits.Single(x=>x.Partition=="VALIDATION");
        Assert.Multiple((Action)(()=>
        {
            Assert.That(report.Version,Is.EqualTo(V9SqlUNetworkBaseBootstrapAudit.Version));
            Assert.That(report.UsesBothEndpointsOfEveryUEdge,Is.True);
            Assert.That(report.TreatsCandidatePairsAsIndependent,Is.False);
            Assert.That(report.IsProductionUncertaintyOrIndependentEss,Is.False);
            Assert.That(report.MayPromoteModelOrBlockingPolicy,Is.False);
            Assert.That(report.PartitionScope,Does.Contain("TEST_FORBIDDEN"));
            Assert.That(train.EligibleBasePeople,Is.EqualTo(4));
            Assert.That(train.BasePeopleTouchingAtLeastOneU,Is.EqualTo(3));
            Assert.That(train.SqlCandidateU,Is.EqualTo(3));
            Assert.That(train.TotalBaseEndpointIncidences,Is.EqualTo(6));
            Assert.That(train.BootstrapReplicates,
                Is.EqualTo(V9SqlUNetworkBaseBootstrapAudit.Replicates));
            Assert.That(train.BootstrapCandidateUP025,Is.GreaterThanOrEqualTo(0m));
            Assert.That(train.BootstrapCandidateUP975,
                Is.GreaterThanOrEqualTo(train.BootstrapCandidateUP025));
            Assert.That(validation.SqlCandidateU,Is.EqualTo(2));
            Assert.That(validation.BasePeopleTouchingAtLeastOneU,Is.EqualTo(3));
            Assert.That(validation.TotalBaseEndpointIncidences,Is.EqualTo(4));
        }));
        var repeat=Analyze(bases:Bases.Reverse().ToArray(),
            edges:UEdges.Reverse().ToArray());
        Assert.That(JsonSerializer.Serialize(report),
            Is.EqualTo(JsonSerializer.Serialize(repeat)));
        var json=JsonSerializer.Serialize(report);
        Assert.Multiple((Action)(()=>
        {
            Assert.That(json,Does.Not.Contain("t-A"));
            Assert.That(json,Does.Not.Contain("v-B"));
            Assert.That(json,Does.Not.Contain("SyntheticBaseId"));
            Assert.That(json,Does.Not.Contain("LowerBaseId"));
            Assert.That(json,Does.Not.Contain("HigherBaseId"));
        }));
    }

    [Test]
    public void Rejects_TEST_duplicate_bases_cross_split_endpoints_self_U_and_wrong_SQL_total()
    {
        Assert.Throws<InvalidDataException>((Action)(()=>
            Analyze(bases:[..Bases,new("TEST","test-base")])));
        Assert.Throws<InvalidDataException>((Action)(()=>
            Analyze(bases:[..Bases,Bases[0]])));
        Assert.Throws<InvalidDataException>((Action)(()=>
            Analyze(edges:[..UEdges,new("TRAIN","t-A","v-B")])));
        Assert.Throws<InvalidDataException>((Action)(()=>
            Analyze(edges:[..UEdges,new("TRAIN","t-A","t-A")])));
        Assert.Throws<InvalidDataException>((Action)(()=>
            Analyze(edges:[..UEdges,new("TEST","t-A","t-B")])));
        Assert.Throws<InvalidDataException>((Action)(()=>
            Analyze(expected:[new("TRAIN",2),new("VALIDATION",2)])));
    }
}
