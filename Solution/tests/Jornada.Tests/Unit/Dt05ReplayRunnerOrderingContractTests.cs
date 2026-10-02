namespace Jornada.Tests.Unit;

public sealed class Dt05ReplayRunnerOrderingContractTests
{
    [Test]
    public void Replay_binding_is_completed_before_score_loop()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root,
            "src", "Jornada.Linkage.Runner", "ProbabilisticLinkageBatchRunner.cs"));

        var materialize = source.IndexOf("CreateRunAndMaterializeUniverseAsync(runId", StringComparison.Ordinal);
        var capture = source.IndexOf("CaptureBronzeSourcesAsync(runId", materialize, StringComparison.Ordinal);
        var publish = source.IndexOf("replayManifestPublisher.PublishAsync(", capture, StringComparison.Ordinal);
        var bind = source.IndexOf("replaySql.RegisterAsync(", publish, StringComparison.Ordinal);
        var scoreLoop = source.IndexOf("while (evaluated < eligible)", bind, StringComparison.Ordinal);

        Assert.Multiple(() => {
            Assert.That(materialize, Is.GreaterThanOrEqualTo(0));
            Assert.That(capture, Is.GreaterThan(materialize));
            Assert.That(publish, Is.GreaterThan(capture));
            Assert.That(bind, Is.GreaterThan(publish));
            Assert.That(scoreLoop, Is.GreaterThan(bind));
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "Jornada.Linkage.Runner",
                "ProbabilisticLinkageBatchRunner.cs");
            if (File.Exists(candidate)) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Solution root não encontrada.");
    }
}
