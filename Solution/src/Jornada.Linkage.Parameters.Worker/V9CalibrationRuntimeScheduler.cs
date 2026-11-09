using System.Diagnostics;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// One operational resource governor for a complete physical calibration.
/// Scientific plans and requested options are never rewritten. CPU is only a
/// concurrency ceiling; RAM, SQL connection pool and disk are independent
/// safety admissions applied together. This is NOT a hard cgroup memory cap.
/// </summary>
public sealed record V9CalibrationRuntimeResourceSample(
    long? EffectiveAvailableMemoryBytes, long? OutputFreeDiskBytes,
    long ResidentBytes, long PeakResidentBytes);

public sealed record V9CalibrationRuntimeSchedulingReceipt(
    string Schema, string Mode, int RequestedWorkers, int SqlPoolMaximum,
    int WorkersCeiling, int PilotConfigurations, long EstimatedIncrementalWorkerBytes,
    long ProcessResidentBeforePilotBytes, long ProcessPeakAfterPilotBytes,
    int MaximumSimultaneousWorkersObserved, int ResourceSamples,
    bool ResourceLimitsAreEstimates, bool ScientificOptionsChanged,
    bool TestUsed, bool GoldUsed, bool Activated);

public sealed class V9CalibrationRuntimeScheduler
{
    public const string Schema = "V9_CALIBRATION_COUPLED_RESOURCE_SCHEDULER_V1";
    private const long MinimumIncrementBytes = 512L * 1024 * 1024;
    private readonly V9CalibrationExecutionResourceBudget _budget;
    private readonly Func<V9CalibrationRuntimeResourceSample> _sample;
    private readonly int _sqlPoolMaximum;
    private long _workerIncrementBytes = MinimumIncrementBytes;
    private int _active;
    private int _maxActive;
    private int _samples;
    private readonly SemaphoreSlim _admission = new(1, 1);

    public V9CalibrationRuntimeScheduler(V9CalibrationExecutionResourceBudget budget,
        string outputDirectory, int sqlPoolMaximum,
        Func<V9CalibrationRuntimeResourceSample>? sampler = null)
    {
        _budget = budget ?? throw new ArgumentNullException(nameof(budget));
        if (sqlPoolMaximum < 1)
            throw new InvalidDataException("V9_RESOURCE_SQL_POOL_INVALID");
        _sqlPoolMaximum = sqlPoolMaximum;
        _sample = sampler ?? (() => Capture(outputDirectory));
    }

    /// <summary>OS sample includes local SQL memory already consumed by the host.
    /// SQL container cap is established by pre-start bootstrap, not by this worker.
    /// No OS statistic is represented as a hard enforcement limit.
    /// </summary>
    public static V9CalibrationRuntimeResourceSample Capture(string outputDirectory)
    {
        var physical = V9CalibrationResourceGovernor.Capture(0, outputDirectory);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new(physical.EffectiveAvailableMemoryBytes, physical.FreeDiskBytes,
            process.WorkingSet64, process.PeakWorkingSet64);
    }

    /// <summary>
    /// Pilot one already-declared physical frame without trimming its hypotheses.
    /// The pilot measures a process-wide peak increment; it cannot prove
    /// independent-worker consumption and is only a conservative estimate.
    /// After the pilot each new worker must pass live RAM/disk admission.
    /// </summary>
    public async Task<(IReadOnlyList<TResult> Results, V9CalibrationRuntimeSchedulingReceipt Receipt)>
        MapAsync<TInput, TResult>(IReadOnlyList<TInput> inputs,
        Func<TInput, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(operation);
        var ceiling = _budget.ConfiguredParallelism > 0
            ? _budget.ConfiguredParallelism
            : _budget.EffectiveParallelism;
        // Reserve one pooled SQL connection for IBGE TF/diagnostics if needed.
        // MaxPoolSize is an upper bound, not a measurement of SQL throughput.
        var sqlCeiling = Math.Max(1, _sqlPoolMaximum - 1);
        if (_budget.ConfiguredParallelism > sqlCeiling && inputs.Count > sqlCeiling)
            throw new InvalidDataException("V9_RESOURCE_SQL_CONNECTION_CAPACITY");
        ceiling = Math.Min(ceiling, sqlCeiling);
        ceiling = Math.Max(1, Math.Min(ceiling, inputs.Count == 0 ? 1 : inputs.Count));
        var sampleBefore = Read();
        RequireDisk(sampleBefore);
        var results = new TResult[inputs.Count];
        if (inputs.Count == 0)
            return (Array.AsReadOnly(results), Receipt(ceiling, sampleBefore.ResidentBytes,
                sampleBefore.PeakResidentBytes, 0));
        // One physical configuration in original declared order. Never a new
        // diagnostic hypothesis, never a pilot on TEST/Gold.
        cancellationToken.ThrowIfCancellationRequested();
        results[0] = await operation(inputs[0], cancellationToken);
        var sampleAfter = Read();
        RequireDisk(sampleAfter);
        var peakIncrement = Math.Max(0, sampleAfter.PeakResidentBytes -
            Math.Max(sampleBefore.ResidentBytes, sampleBefore.PeakResidentBytes));
        _workerIncrementBytes = EstimateIncrementBytes(peakIncrement);
        if (inputs.Count > 1 && _budget.ConfiguredParallelism > 0
            && sampleAfter.EffectiveAvailableMemoryBytes is { } available
            && available < RequiredFreeBytes(ceiling))
            throw new InvalidDataException("V9_RESOURCE_EXPLICIT_WORKERS_EXCEED_MEASURED_RAM");
        if (inputs.Count > 1)
            await Parallel.ForEachAsync(Enumerable.Range(1, inputs.Count - 1),
                new ParallelOptions { CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = ceiling }, async (index, ct) =>
                {
                    await EnterAsync(ct);
                    try { results[index] = await operation(inputs[index], ct); }
                    finally { _admission.Release(); /* released below through Exit, not here */ }
                });
        return (Array.AsReadOnly(results), Receipt(ceiling, sampleBefore.ResidentBytes,
            sampleAfter.PeakResidentBytes, 1));
    }

    public static long EstimateIncrementBytes(long measuredPeakDeltaBytes)
    {
        if (measuredPeakDeltaBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(measuredPeakDeltaBytes));
        if (measuredPeakDeltaBytes > (long.MaxValue / 3) * 2)
            return long.MaxValue;
        return Math.Max(MinimumIncrementBytes, (measuredPeakDeltaBytes * 3 + 1) / 2);
    }

    public static bool CanAdmit(long? measuredAvailableBytes, long? freeDiskBytes,
        int activeWorkers, long incrementalWorkerBytes)
    {
        if (activeWorkers < 0 || incrementalWorkerBytes < 1
            || freeDiskBytes is null
            || freeDiskBytes < V9CalibrationResourceGovernor.MinimumFreeDiskBytes)
            return false;
        if (measuredAvailableBytes is null) return activeWorkers == 0;
        var required = V9CalibrationResourceGovernor.HostReserveBytes
            + (long)(activeWorkers + 1) * incrementalWorkerBytes;
        return required <= measuredAvailableBytes;
    }

    private long RequiredFreeBytes(int workers)
    {
        try
        {
            return checked(V9CalibrationResourceGovernor.HostReserveBytes
                + _workerIncrementBytes * workers);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private V9CalibrationRuntimeResourceSample Read()
    {
        Interlocked.Increment(ref _samples);
        return _sample();
    }

    private static void RequireDisk(V9CalibrationRuntimeResourceSample sample)
    {
        if (sample.OutputFreeDiskBytes is null
            || sample.OutputFreeDiskBytes < V9CalibrationResourceGovernor.MinimumFreeDiskBytes)
            throw new InvalidDataException("V9_RESOURCE_RUNTIME_DISK_BELOW_RESERVE");
    }

    private async Task EnterAsync(CancellationToken token)
    {
        for (;;)
        {
            await _admission.WaitAsync(token);
            var keep = false;
            try
            {
                var now = Read();
                RequireDisk(now);
                if (now.EffectiveAvailableMemoryBytes is { } measured
                    && measured < V9CalibrationResourceGovernor.HostReserveBytes
                        + _workerIncrementBytes)
                    throw new InvalidDataException("V9_RESOURCE_RUNTIME_RAM_BELOW_ONE_WORKER");
                if (CanAdmit(now.EffectiveAvailableMemoryBytes,
                    now.OutputFreeDiskBytes, _active, _workerIncrementBytes))
                {
                    _active++;
                    _maxActive = Math.Max(_maxActive, _active);
                    keep = true;
                }
                else if (_active == 0)
                    throw new InvalidDataException("V9_RESOURCE_RUNTIME_RAM_NOT_ADMISSIBLE");
            }
            finally { _admission.Release(); }
            if (keep) return;
            await Task.Delay(150, token); // wait for an already admitted worker
        }
    }

    private void Exit()
    {
        _admission.Wait();
        try { _active--; }
        finally { _admission.Release(); }
    }

    private V9CalibrationRuntimeSchedulingReceipt Receipt(int ceiling, long before, long after, int pilot)
        => new(Schema, _budget.ConfiguredParallelism == 0 ? "AUTO" : "EXPLICIT",
            _budget.ConfiguredParallelism, _sqlPoolMaximum, ceiling, pilot,
            _workerIncrementBytes, before, after, _maxActive,
            _samples, true, false, false, false, false);
}
