namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt05BlockingProjectionSnapshotContractTests
{
    [Test]
    public void Capture_is_projection_bound_content_addressed_and_pre_score()
    {
        var root=TestContext.CurrentContext.TestDirectory;
        var src=Path.GetFullPath(Path.Combine(root,"..","..","..","..","..","src","Jornada.Linkage.Runner"));
        var publisher=File.ReadAllText(Path.Combine(src,"Dt05BlockingProjectionSnapshotPublisher.cs"));
        var runner=File.ReadAllText(Path.Combine(src,"ProbabilisticLinkageBatchRunner.cs"));
        Assert.Multiple((Action)(() => {
            Assert.That(publisher,Does.Contain("FROM identidade.blocking_chave WITH(HOLDLOCK)"));
            Assert.That(publisher,Does.Contain("normalizacao_versao=@normalizacao"));
            Assert.That(publisher,Does.Contain("projection_schema_version=@schema"));
            Assert.That(publisher,Does.Contain("projection_fingerprint_sha256=@fingerprint"));
            Assert.That(publisher,Does.Contain("semantica_temporal"));
            Assert.That(publisher,Does.Contain("vigencia_fim IS NULL"));
            Assert.That(publisher,Does.Contain("snapshot_kind=\"blocking-projection\""));
            Assert.That(publisher,Does.Contain("objects/{payloadSha[..2]}/{payloadSha}.json"));
            Assert.That(runner.IndexOf("blockingProjectionSnapshotPublisher.CaptureAsync",StringComparison.Ordinal),
                Is.LessThan(runner.IndexOf("ScoreBatchAsync(batch",StringComparison.Ordinal)));
        }));
    }
}
