using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9CalibrationRuntimeSchedulerTests
{
    private const long GiB = V9CalibrationResourceGovernor.GiB;

    private static V9CalibrationRuntimeResourceSample Sample(long? ramGiB, long diskGiB = 10,
        long resident = 512L * 1024 * 1024, long peak = 512L * 1024 * 1024)
        => new(ramGiB * GiB, diskGiB * GiB, resident, peak);

    [Test]
    public void Observed_increment_estimate_has_no_unmeasured_fixed_reserve()
    {
        Assert.That(V9CalibrationRuntimeScheduler.EstimateIncrementBytes(0),
            Is.Zero);
        Assert.That(V9CalibrationRuntimeScheduler.EstimateIncrementBytes(GiB),
            Is.EqualTo(GiB + GiB / 2));
        Assert.That(V9CalibrationRuntimeScheduler.EstimateIncrementBytes(long.MaxValue),
            Is.EqualTo(long.MaxValue));
    }

    [Test]
    public void Live_RAM_disk_and_measured_increment_gate_new_admissions()
    {
        Assert.That(V9CalibrationRuntimeScheduler.CanAdmit(4 * GiB, 8 * GiB, 0, GiB), Is.True);
        Assert.That(V9CalibrationRuntimeScheduler.CanAdmit(3 * GiB, 8 * GiB, 1, GiB), Is.True);
        Assert.That(V9CalibrationRuntimeScheduler.CanAdmit(2 * GiB, 8 * GiB, 1, 0), Is.True);
        Assert.That(V9CalibrationRuntimeScheduler.CanAdmit(2 * GiB - 1, 8 * GiB, 1, 0), Is.False);
        Assert.That(V9CalibrationRuntimeScheduler.CanAdmit(3 * GiB, 8 * GiB, 1, 3 * GiB), Is.False);
        Assert.That(V9CalibrationRuntimeScheduler.CanAdmit(8 * GiB, 2 * GiB, 0, GiB), Is.False);
        Assert.That(V9CalibrationRuntimeScheduler.CanAdmit(null, 8 * GiB, 0, 0), Is.True);
        Assert.That(V9CalibrationRuntimeScheduler.CanAdmit(null, 8 * GiB, 1, 0), Is.False);
        Assert.That(V9CalibrationRuntimeScheduler.CanAdmit(long.MaxValue, 8 * GiB,
            int.MaxValue, long.MaxValue), Is.False);
    }

    [Test]
    public async Task Automatic_mode_runs_each_declared_request_without_blocking_pilot()
    {
        var budget = V9CalibrationResourceGovernor.Decide(0, 4, 12 * GiB, 10 * GiB, 9 * GiB);
        var scheduler = new V9CalibrationRuntimeScheduler(budget, ".", 20,
            () => Sample(10));
        var visited = new List<int>();
        var (result, receipt) = await scheduler.MapAsync(new[] { 1, 2, 3, 4, 5 },
            async (x, ct) =>
            {
                lock (visited) visited.Add(x);
                await Task.Delay(1, ct);
                return x * 10;
            }, CancellationToken.None);
        Assert.That(result, Is.EqualTo(new[] { 10, 20, 30, 40, 50 }));
        Assert.That(visited.OrderBy(x => x), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(visited, Has.Count.EqualTo(5));
        Assert.That(receipt.PilotConfigurations, Is.Zero);
        Assert.That(receipt.WorkersCeiling, Is.LessThanOrEqualTo(budget.EffectiveParallelism));
        Assert.That(receipt.ScientificOptionsChanged, Is.False);
        Assert.That(receipt.TestUsed, Is.False);
        Assert.That(receipt.GoldUsed, Is.False);
        Assert.That(receipt.Activated, Is.False);
        Assert.That(scheduler.Receipts, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Second_worker_can_start_while_first_large_group_is_still_running()
    {
        var budget = V9CalibrationResourceGovernor.Decide(0, 4, 12 * GiB, null, 9 * GiB);
        var scheduler = new V9CalibrationRuntimeScheduler(budget, ".", 20,
            () => Sample(10));
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = scheduler.MapAsync(new[] { 0, 1, 2 }, async (i, ct) =>
        {
            if (i == 0)
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task.WaitAsync(ct);
            }
            if (i == 1) secondStarted.TrySetResult();
            return i;
        }, CancellationToken.None);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
        finally { releaseFirst.TrySetResult(); }
        var (values, receipt) = await running;
        Assert.That(values, Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(receipt.PilotConfigurations, Is.Zero);
        Assert.That(receipt.MaximumSimultaneousWorkersObserved, Is.GreaterThanOrEqualTo(2));
        Assert.That(receipt.EstimatedIncrementalWorkerBytes, Is.Zero);
    }

    [Test]
    public void Explicit_worker_count_remains_the_declared_ceiling()
    {
        var budget = V9CalibrationResourceGovernor.Decide(8, 4, 8 * GiB, null, 9 * GiB);
        var scheduler = new V9CalibrationRuntimeScheduler(budget, ".", 20,
            () => Sample(5));
        var (values, receipt) = scheduler.MapAsync(Enumerable.Range(0, 9).ToArray(),
            (i, _) => Task.FromResult(i), CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(values, Is.EqualTo(Enumerable.Range(0, 9).ToArray()));
        Assert.That(receipt.WorkersCeiling, Is.EqualTo(8));
        Assert.That(receipt.PilotConfigurations, Is.Zero);
        Assert.That(budget.ConfiguredParallelism, Is.EqualTo(8));
    }

    [Test]
    public void Explicit_multiple_workers_with_unmeasured_RAM_rejects_instead_of_serial_fallback()
    {
        var budget = V9CalibrationResourceGovernor.Decide(4, 8, null, null, 10 * GiB);
        var scheduler = new V9CalibrationRuntimeScheduler(budget, ".", 20,
            () => Sample(null));
        var called = 0;
        var failure = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
        {
            await scheduler.MapAsync(new[] { 0, 1, 2, 3 },
                (i, _) => { Interlocked.Increment(ref called); return Task.FromResult(i); },
                CancellationToken.None);
        }));
        Assert.That(failure!.Message, Does.Contain("RAM_UNMEASURED"));
        Assert.That(called, Is.Zero);
    }

    [Test]
    public void Disk_budget_tracks_corresponding_size_of_declared_corpus()
    {
        Assert.That(V9CalibrationRuntimeScheduler.EstimateRequiredDiskBytes(0), Is.EqualTo(3 * GiB));
        Assert.That(V9CalibrationRuntimeScheduler.EstimateRequiredDiskBytes(2 * GiB), Is.EqualTo(7 * GiB));
        Assert.That(V9CalibrationRuntimeScheduler.EstimateRequiredDiskBytes(long.MaxValue),
            Is.EqualTo(long.MaxValue));
    }

    [Test]
    public void Sql_pool_occupancy_caps_auto_and_rejects_explicit_excess()
    {
        var budget = V9CalibrationResourceGovernor.Decide(0, 8, 18 * GiB, null, 10 * GiB);
        var auto = new V9CalibrationRuntimeScheduler(budget, ".", 3, () => Sample(15));
        var (values, receipt) = auto.MapAsync(Enumerable.Range(0, 5).ToArray(),
            (i, _) => Task.FromResult(i), CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(values.Count, Is.EqualTo(5));
        Assert.That(receipt.WorkersCeiling, Is.EqualTo(2)); // reserve one SQL session
        var explicitBudget = V9CalibrationResourceGovernor.Decide(4, 8, 18 * GiB, null, 10 * GiB);
        var explicitScheduler = new V9CalibrationRuntimeScheduler(explicitBudget, ".", 3, () => Sample(15));
        var failure = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
        {
            await explicitScheduler.MapAsync(new[] { 0, 1, 2, 3 },
                (i, _) => Task.FromResult(i), CancellationToken.None);
        }));
        Assert.That(failure!.Message, Does.Contain("SQL_CONNECTION"));
    }

    [Test]
    public void Runtime_disk_drop_fails_closed_between_pilot_and_concurrent_fits()
    {
        var budget = V9CalibrationResourceGovernor.Decide(0, 4, 15 * GiB, null, 9 * GiB);
        var reads = 0;
        var scheduler = new V9CalibrationRuntimeScheduler(budget, ".", 20,
            () => Sample(10, Interlocked.Increment(ref reads) <= 1 ? 9 : 1));
        var called = 0;
        var failure = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
        {
            await scheduler.MapAsync(new[] { 0, 1, 2 },
                (i, _) => { Interlocked.Increment(ref called); return Task.FromResult(i); },
                CancellationToken.None);
        }));
        Assert.That(failure!.Message, Does.Contain("DISK_BELOW_RESERVE"));
        Assert.That(called, Is.EqualTo(1));
    }
}
