namespace Jornada.Tests.Unit;

[TestFixture,Category("Unit")]
public sealed class Dt05DeterministicHistoricalReplayContractTests
{
    private static string Root(){var d=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);while(d is not null&&!Directory.Exists(Path.Combine(d.FullName,"database","migrations")))d=d.Parent;return d?.FullName??throw new DirectoryNotFoundException();}
    [Test]
    public void Replay_uses_exact_source_universe_model_and_never_recaptures_current_state()
    {
        var root=Root();
        var runner=File.ReadAllText(Path.Combine(root,"src","Jornada.Linkage.Runner","ProbabilisticLinkageBatchRunner.cs"));
        var sql=File.ReadAllText(Path.Combine(root,"src","Jornada.Linkage.Runner","Dt05ReplaySql.cs"));
        Assert.Multiple((Action)(()=>{
            Assert.That(sql,Does.Contain("ReadHistoricalRunIdentityAsync"));
            Assert.That(sql,Does.Contain("source run precisa estar PUBLICADO"));
            Assert.That(runner,Does.Contain("replayIdentity.ModelVersion"));
            Assert.That(runner,Does.Contain("model.ModelId != replayIdentity.ModelId"));
            Assert.That(runner,Does.Contain("li.linkage_run_id=@replay_source_run_id"));
            Assert.That(runner,Does.Contain("REPLAY histórico não aceita filtros que alterem o universo"));
            Assert.That(runner,Does.Contain("request.Mode != LinkageRunType.REPLAY"));
        }));
    }
}
