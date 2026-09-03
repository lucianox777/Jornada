using Jornada.Operations.Maintenance.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class PipelineWatchdogTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 20, 0, 0, TimeSpan.Zero);

    [Test]
    public void Evaluator_reports_only_conditions_beyond_thresholds()
    {
        var options = new PipelineWatchdogOptions
        {
            LinkageRunMaxMinutes = 60,
            ModelGenerationMaxMinutes = 90,
            ExpiredLeaseGraceMinutes = 5,
            PendingBacklogMaxAgeMinutes = 30,
            InitialLoadMaxHours = 12
        };
        var snapshot = new PipelineWatchdogSnapshot(
            Now,
            ActiveLinkageRuns: 1,
            OldestActiveLinkageRunStartedAt: Now.AddMinutes(-61),
            GeneratingModels: 1,
            OldestGeneratingModelStartedAt: Now.AddMinutes(-30),
            ExpiredActiveLeases: 2,
            OldestExpiredLeaseAt: Now.AddMinutes(-6),
            PendingLots: 10,
            OldestPendingLotCreatedAt: Now.AddMinutes(-31),
            InitialLoadActive: true,
            InitialLoadActivatedAt: Now.AddHours(-13));

        var findings = PipelineWatchdogEvaluator.Evaluate(snapshot, options);

        Assert.Multiple(() =>
        {
            Assert.That(findings.Any(f => f.Code == "LINKAGE_RUN_STALE"), Is.True);
            Assert.That(findings.Any(f => f.Code == "LINKAGE_MODEL_GENERATION_STALE"), Is.False);
            Assert.That(findings.Any(f => f.Code == "PROCESSOR_LEASE_EXPIRED"), Is.True);
            Assert.That(findings.Any(f => f.Code == "PROCESSOR_BACKLOG_OLD"), Is.True);
            Assert.That(findings.Any(f => f.Code == "INITIAL_LOAD_MODE_STALE"), Is.True);
        });
    }

    [Test]
    public void Evaluator_returns_empty_when_pipeline_is_within_operational_limits()
    {
        var options = new PipelineWatchdogOptions();
        var snapshot = new PipelineWatchdogSnapshot(
            Now,
            ActiveLinkageRuns: 1,
            OldestActiveLinkageRunStartedAt: Now.AddMinutes(-30),
            GeneratingModels: 0,
            OldestGeneratingModelStartedAt: null,
            ExpiredActiveLeases: 0,
            OldestExpiredLeaseAt: null,
            PendingLots: 2,
            OldestPendingLotCreatedAt: Now.AddMinutes(-10),
            InitialLoadActive: false,
            InitialLoadActivatedAt: null);

        Assert.That(PipelineWatchdogEvaluator.Evaluate(snapshot, options), Is.Empty);
    }

    [Test]
    public void Evaluator_does_not_infer_abandoned_sql_applocks()
    {
        var snapshot = new PipelineWatchdogSnapshot(
            Now,
            ActiveLinkageRuns: 0,
            OldestActiveLinkageRunStartedAt: null,
            GeneratingModels: 0,
            OldestGeneratingModelStartedAt: null,
            ExpiredActiveLeases: 0,
            OldestExpiredLeaseAt: null,
            PendingLots: 0,
            OldestPendingLotCreatedAt: null,
            InitialLoadActive: false,
            InitialLoadActivatedAt: null);

        var findings = PipelineWatchdogEvaluator.Evaluate(snapshot, new PipelineWatchdogOptions());
        Assert.That(findings.Any(f => f.Code.Contains("LOCK", StringComparison.OrdinalIgnoreCase)), Is.False);
    }
}
