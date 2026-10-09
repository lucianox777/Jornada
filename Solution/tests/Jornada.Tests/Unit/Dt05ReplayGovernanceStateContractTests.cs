namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt05ReplayGovernanceStateContractTests
{
    private static string Root()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null && !Directory.Exists(Path.Combine(dir.FullName,"database","migrations"))) dir=dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Solution root");
    }

    [Test]
    public void Governance_identity_is_create_once_and_captured_before_manifest_and_score()
    {
        var root=Root();
        var migration=File.ReadAllText(Path.Combine(root,"database","migrations","20261002_Linkage_Replay_Governanca_DT05.sql"));
        var runner=File.ReadAllText(Path.Combine(root,"src","Jornada.Linkage.Runner","ProbabilisticLinkageBatchRunner.cs"));
        Assert.Multiple((Action)(() => {
            Assert.That(migration,Does.Contain("linkage_replay_estado_governanca"));
            Assert.That(migration,Does.Contain("INSTEAD OF UPDATE,DELETE"));
            Assert.That(migration,Does.Contain("estado_identidade=N'REFERENCIA'"));
            Assert.That(migration,Does.Contain("decisao_identidade_evento_id"));
            Assert.That(migration,Does.Contain("HASHBYTES('SHA2_256'"));
            var capture=runner.IndexOf("CaptureGovernanceStateAsync",StringComparison.Ordinal);
            var manifest=runner.IndexOf("replayManifestPublisher.PublishAsync",StringComparison.Ordinal);
            var score=runner.IndexOf("while (evaluated < eligible)",StringComparison.Ordinal);
            Assert.That(capture,Is.GreaterThanOrEqualTo(0));
            Assert.That(manifest,Is.GreaterThan(capture));
            Assert.That(score,Is.GreaterThan(manifest));
        }));
    }
}
