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
    long MinimumOutputFreeDiskBytes, long InputCorpusBytes,
    string SqlCapacityBasis,
    bool ResourceLimitsAreEstimates, bool ScientificOptionsChanged,
    bool TestUsed, bool GoldUsed, bool Activated);

public sealed class V9CalibrationRuntimeScheduler
{
    public const string Schema = "V9_CALIBRATION_COUPLED_RESOURCE_SCHEDULER_V2";
    private static readonly TimeSpan AdmissionSpacing = TimeSpan.FromSeconds(1);
    private readonly V9CalibrationExecutionResourceBudget _budget;
    private readonly Func<V9CalibrationRuntimeResourceSample> _sample;
    private readonly int _sqlPoolMaximum;
    private readonly long _requiredFreeDiskBytes;
    private readonly long _inputCorpusBytes;
    // Observed live drop since a prior admission; NOT a fixed RAM reserve.
    private long _workerIncrementBytes;
    private long? _availableAtLastAdmission;
    private long _peakAtLastAdmission;
    private DateTimeOffset _lastAdmissionUtc = DateTimeOffset.MinValue;
    private int _active;
    private int _maxActive;
    private int _samples;
    private readonly object _admissionGate = new();
    private readonly List<V9CalibrationRuntimeSchedulingReceipt> _receipts = [];

    public IReadOnlyList<V9CalibrationRuntimeSchedulingReceipt> Receipts => _receipts.AsReadOnly();

    public V9CalibrationRuntimeScheduler(V9CalibrationExecutionResourceBudget budget,
        string outputDirectory, int sqlPoolMaximum,
        Func<V9CalibrationRuntimeResourceSample>? sampler = null,
        string? sourcePath = null)
    {
        _budget = budget ?? throw new ArgumentNullException(nameof(budget));
        if (sqlPoolMaximum < 1)
            throw new InvalidDataException("V9_RESOURCE_SQL_POOL_INVALID");
        _sqlPoolMaximum = sqlPoolMaximum;
        _inputCorpusBytes = sourcePath is not null && File.Exists(sourcePath)
            ? new FileInfo(sourcePath).Length : 0;
        _requiredFreeDiskBytes = EstimateRequiredDiskBytes(_inputCorpusBytes);
        _sample = sampler ?? (() => Capture(outputDirectory));
    }

    /// <summary>OS sample includes local SQL memory already consumed by the host.
    /// SQL container cap is established by pre-start bootstrap, not by this worker.
    /// No OS statistic is represented as a hard enforcement limit.
    /// </summary>
    public static V9CalibrationRuntimeResourceSample Capture(string outputDirectory)
    {
        var physical = V9CalibrationResourceGovernor.CaptureLiveResources(outputDirectory);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new(physical.EffectiveAvailableMemoryBytes, physical.FreeDiskBytes,
            process.WorkingSet64, process.PeakWorkingSet64);
    }

    /// <summary>
    /// Admit physical groups just-in-time using measured MemAvailable/cgroup and
    /// disk. No complete SQL+replay group is a serial pilot. A short stagger
    /// between worker starts reveals memory pressure without waiting for the
    /// preceding 64/128-hypothesis group to finish.
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
        // Spare one pooled SQL connection for TF/diagnostics, NOT a spare CPU.
        var sqlCeiling = Math.Max(1, _sqlPoolMaximum - 1);
        if (_budget.ConfiguredParallelism > sqlCeiling && inputs.Count > sqlCeiling)
            throw new InvalidDataException("V9_RESOURCE_SQL_CONNECTION_CAPACITY");
        ceiling = Math.Min(ceiling, sqlCeiling);
        ceiling = Math.Max(1, Math.Min(ceiling, inputs.Count == 0 ? 1 : inputs.Count));
        var sampleBefore = Read();
        RequireDisk(sampleBefore);
        if (inputs.Count > 1 && _budget.ConfiguredParallelism > 1 && ceiling > 1
            && sampleBefore.EffectiveAvailableMemoryBytes is null)
            throw new InvalidDataException("V9_RESOURCE_EXPLICIT_RAM_UNMEASURED");
        var results = new TResult[inputs.Count];
        if (inputs.Count > 0)
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, inputs.Count),
                new ParallelOptions { CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = ceiling }, async (index, ct) =>
                {
                    await EnterAsync(ct);
                    try { results[index] = await operation(inputs[index], ct); }
                    finally { Exit(); }
                });
        }
        var sampleAfter = Read();
        RequireDisk(sampleAfter);
        // Backward-compatible receipt field names: PilotConfigurations=0 means
        // no blocking pilot. Peak is measured after scheduling, not a pilot.
        var receipt = Receipt(ceiling, sampleBefore.ResidentBytes,
            sampleAfter.PeakResidentBytes, 0);
        _receipts.Add(receipt);
        return (Array.AsReadOnly(results), receipt);
    }

    /// <summary>Baseline output/scratch disk reservation from actual input size.
    /// SQL Docker overlay may live on a different filesystem and still requires
    /// the external pre-start bootstrap admission. This is a heuristic, not a
    /// guarantee that all temporary SQL files will fit.
    /// </summary>
    public static long EstimateRequiredDiskBytes(long inputCorpusBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputCorpusBytes);
        if (inputCorpusBytes > (long.MaxValue - V9CalibrationResourceGovernor.GiB) / 3)
            return long.MaxValue;
        return Math.Max(V9CalibrationResourceGovernor.MinimumFreeDiskBytes,
            inputCorpusBytes * 3 + V9CalibrationResourceGovernor.GiB);
    }

    // Apply a 1.5x cushion only to POSITIVE observed memory growth.
    public static long EstimateIncrementBytes(long measuredPeakDeltaBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(measuredPeakDeltaBytes);
        if (measuredPeakDeltaBytes > (long.MaxValue - 1) / 3)
            return long.MaxValue;
        return (measuredPeakDeltaBytes * 3 + 1) / 2;
    }

    public static bool CanAdmit(long? measuredAvailableBytes, long? freeDiskBytes,
        int activeWorkers, long incrementalWorkerBytes)
    {
        if (activeWorkers < 0 || incrementalWorkerBytes < 0
            || freeDiskBytes is null
            || freeDiskBytes < V9CalibrationResourceGovernor.MinimumFreeDiskBytes)
            return false;
        if (measuredAvailableBytes is null) return activeWorkers == 0;
        // Startup already enforced the 2 GiB preventive floor. After SQL/bootstrap
        // consumes RAM, permit the first worker above the 1 GiB critical floor;
        // retain the 2 GiB preventive floor for additional concurrent workers.
        // The OS controls actual allocation; no per-worker RAM is pre-reserved.
        var admissionFloor = activeWorkers == 0
            ? V9CalibrationResourceGovernor.CriticalAvailableMemoryBytes
            : V9CalibrationResourceGovernor.PreventiveAvailableMemoryBytes;
        if (measuredAvailableBytes < admissionFloor)
            return false;
        if (incrementalWorkerBytes >
            long.MaxValue - V9CalibrationResourceGovernor.CriticalAvailableMemoryBytes)
            return false;
        var required = Math.Max(admissionFloor,
            V9CalibrationResourceGovernor.CriticalAvailableMemoryBytes + incrementalWorkerBytes);
        return measuredAvailableBytes >= required;
    }

    private V9CalibrationRuntimeResourceSample Read()
    {
        Interlocked.Increment(ref _samples);
        return _sample();
    }

    private void RequireDisk(V9CalibrationRuntimeResourceSample sample)
    {
        if (sample.OutputFreeDiskBytes is null
            || sample.OutputFreeDiskBytes < _requiredFreeDiskBytes)
            throw new InvalidDataException("V9_RESOURCE_RUNTIME_DISK_BELOW_RESERVE");
    }

    private async Task EnterAsync(CancellationToken token)
    {
        for (;;)
        {
            token.ThrowIfCancellationRequested();
            bool admitted = false;
            lock (_admissionGate)
            {
                var now = Read();
                RequireDisk(now);
                var utc = DateTimeOffset.UtcNow;
                // Stagger worker starts by one second, not by an entire
                // SQL+replay group. No extra hypotheses or physical probes.
                if (_active > 0 && utc - _lastAdmissionUtc < AdmissionSpacing)
                {
                    // Wait briefly to observe current RAM pressure.
                }
                else
                {
                    if (_active > 0 && now.EffectiveAvailableMemoryBytes is { } available
                        && _availableAtLastAdmission is { } previousAvailable)
                    {
                        var drop = Math.Max(0L, previousAvailable - available);
                        var processPeakChange = Math.Max(0L,
                            now.PeakResidentBytes - _peakAtLastAdmission);
                        _workerIncrementBytes = Math.Max(_workerIncrementBytes,
                            EstimateIncrementBytes(Math.Max(drop, processPeakChange)));
                    }
                    if (CanAdmit(now.EffectiveAvailableMemoryBytes,
                        now.OutputFreeDiskBytes, _active, _workerIncrementBytes))
                    {
                        _active++;
                        _maxActive = Math.Max(_maxActive, _active);
                        _availableAtLastAdmission = now.EffectiveAvailableMemoryBytes;
                        _peakAtLastAdmission = now.PeakResidentBytes;
                        _lastAdmissionUtc = utc;
                        admitted = true;
                    }
                    else if (_active == 0)
                        throw new InvalidDataException(
                            now.EffectiveAvailableMemoryBytes is { } ram
                                && ram < V9CalibrationResourceGovernor.CriticalAvailableMemoryBytes
                            ? "V9_RESOURCE_RUNTIME_RAM_CRITICAL"
                            : "V9_RESOURCE_RUNTIME_RAM_NOT_ADMISSIBLE");
                }
            }
            if (admitted) return;
            await Task.Delay(250, token);
        }
    }

    private void Exit()
    {
        lock (_admissionGate)
        {
            _active--;
            if (_active == 0)
            {
                // Do not carry this transient estimate into another batch.
                _workerIncrementBytes = 0;
                _availableAtLastAdmission = null;
                _peakAtLastAdmission = 0;
                _lastAdmissionUtc = DateTimeOffset.MinValue;
            }
        }
    }

    private V9CalibrationRuntimeSchedulingReceipt Receipt(int ceiling, long before, long after, int pilot)
        => new(Schema, _budget.ConfiguredParallelism == 0 ? "AUTO" : "EXPLICIT",
            _budget.ConfiguredParallelism, _sqlPoolMaximum, ceiling, pilot,
            _workerIncrementBytes, before, after, Math.Max(pilot, _maxActive),
            _samples, _requiredFreeDiskBytes, _inputCorpusBytes,
            "SQL_POOL_UPPER_BOUND__DOCKER_BOOTSTRAP_EXTERNAL_NOT_A_THROUGHPUT_PROOF",
            true, false, false, false, false);
}
