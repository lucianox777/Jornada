namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt05CandidateStateSnapshotContractTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "database", "migrations")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Solution root");
    }

    [Test]
    public void Runner_freezes_candidate_state_after_governance_and_before_manifest_and_score()
    {
        var root = Root();
        var publisher = File.ReadAllText(Path.Combine(root, "src", "Jornada.Linkage.Runner",
            "Dt05CandidateStateSnapshotPublisher.cs"));
        var runner = File.ReadAllText(Path.Combine(root, "src", "Jornada.Linkage.Runner",
            "ProbabilisticLinkageBatchRunner.cs"));
        var project = File.ReadAllText(Path.Combine(root, "src", "Jornada.Linkage.Runner",
            "Jornada.Linkage.Runner.csproj"));

        Assert.Multiple(() =>
        {
            Assert.That(project, Does.Contain("Parquet.Net").And.Contain("6.1.0"));
            Assert.That(publisher, Does.Contain("CompressionMethod.Zstd"));
            Assert.That(publisher, Does.Contain("candidate-state"));
            Assert.That(publisher, Does.Contain("estado_identidade=N'REFERENCIA'"));
            Assert.That(publisher, Does.Contain("Encoding.Unicode.GetBytes(canonical)"));
            Assert.That(publisher, Does.Contain("estado candidato mudou após a captura de governança"));
            Assert.That(publisher, Does.Not.Contain("nome_completo = x.NomeCompleto").After("var document = new"));
            var governance = runner.IndexOf("CaptureGovernanceStateAsync", StringComparison.Ordinal);
            var candidateState = runner.IndexOf("candidateStateSnapshotPublisher.CaptureAsync", StringComparison.Ordinal);
            var manifest = runner.IndexOf("replayManifestPublisher.PublishAsync", StringComparison.Ordinal);
            var score = runner.IndexOf("while (evaluated < eligible)", StringComparison.Ordinal);
            Assert.That(candidateState, Is.GreaterThan(governance));
            Assert.That(manifest, Is.GreaterThan(candidateState));
            Assert.That(score, Is.GreaterThan(manifest));
        });
    }
}
