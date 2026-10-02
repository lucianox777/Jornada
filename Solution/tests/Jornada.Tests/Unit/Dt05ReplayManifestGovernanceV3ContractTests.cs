namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt05ReplayManifestGovernanceV3ContractTests
{
    private static string Root()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null && !Directory.Exists(Path.Combine(dir.FullName,"database","migrations"))) dir=dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Solution root");
    }

    [Test]
    public void Manifest_v3_binds_create_once_governance_identity_before_score()
    {
        var root=Root();
        var migration=File.ReadAllText(Path.Combine(root,"database","migrations","20261002_Linkage_Replay_Manifest_Governanca_DT05.sql"));
        var sql=File.ReadAllText(Path.Combine(root,"src","Jornada.Linkage.Runner","Dt05ReplaySql.cs"));
        var publisher=File.ReadAllText(Path.Combine(root,"src","Jornada.Linkage.Runner","Dt05ReplayManifestPublisher.cs"));
        var runner=File.ReadAllText(Path.Combine(root,"src","Jornada.Linkage.Runner","ProbabilisticLinkageBatchRunner.cs"));
        Assert.Multiple(() => {
            Assert.That(migration,Does.Contain("schema_version IN(1,2,3)"));
            Assert.That(migration,Does.Contain("linkage_replay_estado_governanca"));
            Assert.That(migration,Does.Contain("THROW 51985"));
            Assert.That(sql,Does.Contain("@schema_version=3"));
            Assert.That(publisher,Does.Contain("schema_version = 3"));
            Assert.That(publisher,Does.Contain("candidatos_sha256 = identity.CandidateSetSha256"));
            var capture=runner.IndexOf("CaptureGovernanceStateAsync",StringComparison.Ordinal);
            var preparation=runner.IndexOf("ReadPreparationAsync",StringComparison.Ordinal);
            var manifest=runner.IndexOf("replayManifestPublisher.PublishAsync",StringComparison.Ordinal);
            var score=runner.IndexOf("while (evaluated < eligible)",StringComparison.Ordinal);
            Assert.That(preparation,Is.GreaterThan(capture));
            Assert.That(manifest,Is.GreaterThan(preparation));
            Assert.That(score,Is.GreaterThan(manifest));
        });
    }
}
