using System.Diagnostics;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Best-effort, read-only, non-inferential sampling of logical file lengths
/// belonging to this calibration's fresh output tree. This is not physical
/// blocks, host disk capacity or SQL container storage.
/// </summary>
public sealed record V9DiskHighWaterDiagnostic(
    string Schema, string Status, long? BaselineLogicalBytes, long? CurrentLogicalBytes,
    long? PeakLogicalBytes, long? PeakAboveBaselineLogicalBytes,
    long? PeakObservedAtMilliseconds, int SuccessfulSamples, int FailedSamples,
    int SamplingIntervalMilliseconds, long ObservationElapsedMilliseconds,
    string MeasurementScope, string Exclusions, string Note);

public sealed class V9CalibrationDiskSampler : IAsyncDisposable
{
    private readonly string _root;
    private readonly Func<string, long> _readLogicalBytes;
    private readonly int _intervalMilliseconds;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Task _task;
    private readonly Stopwatch _timer = Stopwatch.StartNew();
    private readonly object _gate = new();
    private long? _baseline;
    private long? _current;
    private long? _peak;
    private long? _peakAt;
    private int _success;
    private int _failed;

    public V9CalibrationDiskSampler(string newOutputDirectory, TimeSpan interval,
        Func<string, long>? readLogicalBytes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newOutputDirectory);
        if (interval < TimeSpan.FromMilliseconds(10) || interval > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(interval));
        _root = Path.GetFullPath(newOutputDirectory);
        _intervalMilliseconds = checked((int)interval.TotalMilliseconds);
        _readLogicalBytes = readLogicalBytes ?? ReadOutputLogicalBytes;
        ObserveNow(); // Baseline includes the reserved 10KiB and execution.lock.
        _task = Task.Run(MeasurePeriodicallyAsync);
    }

    public static long ReadOutputLogicalBytes(string root)
    {
        long total = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        // No contents are read and no paths are serialized; avoid symlink traversal.
        foreach (var file in Directory.EnumerateFiles(root, "*", options))
            total = checked(total + new FileInfo(file).Length);
        return total;
    }

    /// <summary>Explicit boundary sample complements fixed-period samples.</summary>
    public void ObserveNow()
    {
        try
        {
            var bytes = _readLogicalBytes(_root);
            if (bytes < 0) throw new InvalidDataException("Invalid disk diagnostic value.");
            lock (_gate)
            {
                if (_success == 0) _baseline = bytes;
                _success++;
                _current = bytes;
                if (_peak is null || bytes > _peak)
                {
                    _peak = bytes;
                    _peakAt = _timer.ElapsedMilliseconds;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or OverflowException)
        {
            // Telemetry MUST NOT alter scientific outcomes or leak private paths/errors.
            lock (_gate) _failed++;
        }
    }

    private async Task MeasurePeriodicallyAsync()
    {
        try
        {
            while (!_cancel.IsCancellationRequested)
            {
                await Task.Delay(_intervalMilliseconds, _cancel.Token);
                ObserveNow();
            }
        }
        catch (OperationCanceledException) { /* normal cancellation after final sample */ }
    }

    public V9DiskHighWaterDiagnostic Snapshot()
    {
        ObserveNow();
        lock (_gate)
        {
            var state = _success == 0 ? "NOT_MEASURED" : _failed > 0
                ? "PARTIAL_SAMPLED" : "MEASURED_SAMPLED";
            return new("V9_DISK_OUTPUT_LOGICAL_HIGH_WATER_V1",
                state, _baseline, _current, _peak,
                _baseline is { } b && _peak is { } p ? Math.Max(0, p - b) : null,
                _peakAt, _success, _failed, _intervalMilliseconds, _timer.ElapsedMilliseconds,
                "FRESH_OUTPUT_DIRECTORY_RECURSIVE_LOGICAL_FILE_LENGTHS_ONLY",
                "SQL_CONTAINER, EXTERNAL_TMP, CORPUS_INPUT, VOLUME_USED_FREE, PHYSICAL_ALLOCATED_BLOCKS: NOT_MEASURED",
                "Periodic best-effort samples plus explicit boundaries; short-lived files between samples may be missed. No causal attribution of shared-volume usage; peak is NOT an upper bound on physical disk consumed.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cancel.Cancel();
        try { await _task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        finally { _cancel.Dispose(); }
    }
}
