using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture,Category("Unit")]
public sealed class Dt05HistoricalBlockingProjectionContractTests
{
    private static string Root(){var d=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);while(d is not null&&!Directory.Exists(Path.Combine(d.FullName,"database","migrations")))d=d.Parent;return d?.FullName??throw new DirectoryNotFoundException();}
    [Test]
    public void Manifest_v4_binds_and_replay_verifies_blocking_projection()
    {
        var root=Root();
        var migration=File.ReadAllText(Path.Combine(root,"database","migrations","20261003_Linkage_Replay_Blocking_Projection_Binding_DT05.sql"));
        var sql=File.ReadAllText(Path.Combine(root,"src","Jornada.Linkage.Runner","Dt05ReplaySql.cs"));
        var runner=File.ReadAllText(Path.Combine(root,"src","Jornada.Linkage.Runner","ProbabilisticLinkageBatchRunner.cs"));
        var linkage=File.ReadAllText(Path.Combine(root,"src","Jornada.Linkage.Runner","SqlProbabilisticIdentityLinkage.cs"));
        Assert.Multiple(()=>{
            Assert.That(migration,Does.Contain("schema v4 exige binding físico válido da projeção de blocking"));
            Assert.That(sql,Does.Contain("ReadHistoricalBlockingProjectionBindingAsync"));
            Assert.That(sql,Does.Contain("@schema_version=4"));
            Assert.That(runner,Does.Contain("historicalBlockingProjectionVerifier.VerifyAsync"));
            Assert.That(linkage,Does.Contain("UseHistoricalBlockingProjection"));
            Assert.That(linkage,Does.Contain("passSet.IntersectWith"));
            Assert.That(linkage,Does.Contain("matching.UnionWith"));
            Assert.That(linkage,Does.Not.Contain("replay histórico com ruleset dinâmico exige consumo da projeção de blocking congelada"));
        });
    }
}
