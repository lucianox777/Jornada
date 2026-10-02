using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class LinkageRunOptionsTests
{
    [Test]
    public void Parses_schedulable_execution_parameters()
    {
        var options = LinkageRunOptions.Parse(new[]
        {
            "--mode", "REPLAY",
            "--model-version", "12",
            "--gestor", "SMADS",
            "--batch-size", "20000",
            "--max-parallelism", "4",
            "--max-records", "500000",
            "--publish", "true",
            "--replay-source-run-id", "11111111-2222-3333-4444-555555555555"
        });

        Assert.Multiple(() =>
        {
            Assert.That(options.Mode, Is.EqualTo(LinkageRunType.REPLAY));
            Assert.That(options.ModelVersion, Is.EqualTo(12));
            Assert.That(options.GestorCodigo, Is.EqualTo("SMADS"));
            Assert.That(options.BatchSize, Is.EqualTo(20000));
            Assert.That(options.MaxParallelism, Is.EqualTo(4));
            Assert.That(options.MaxRecords, Is.EqualTo(500000));
            Assert.That(options.Publish, Is.True);
            Assert.That(options.ReplaySourceRunId, Is.EqualTo(Guid.Parse("11111111-2222-3333-4444-555555555555")));
        });
    }

    [Test]
    public void Replay_requires_explicit_historical_source_run()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            LinkageRunOptions.Parse(new[] { "--mode", "REPLAY" }));
        Assert.That(error!.Message, Does.Contain("replay-source-run-id"));
    }

    [Test]
    public void Replay_source_run_is_rejected_outside_replay()
    {
        Assert.Throws<InvalidOperationException>(() => LinkageRunOptions.Parse(new[]
        {
            "--mode", "ON_DEMAND", "--replay-source-run-id", "11111111-2222-3333-4444-555555555555"
        }));
    }

    [Test]
    public void Model_validation_cannot_publish()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LinkageRunOptions.Parse(new[] { "--mode", "MODEL_VALIDATION", "--publish", "true" }));
    }
}
