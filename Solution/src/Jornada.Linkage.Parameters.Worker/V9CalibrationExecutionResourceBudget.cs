using System.Globalization;
using System.Runtime.InteropServices;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Resource admission belonging to EVERY physical V9 calibration invocation.
/// Scheduling only: never modifies a signed plan, candidate set, scientific
/// thresholds, M/U/TF, statistical multiplicity, or TEST/Gold boundaries.
/// The Docker SQL budget is planned BEFORE SQL starts by its separate runner
/// bootstrap; a process launched after SQL cannot resize its container.
/// </summary>
public sealed record V9CalibrationExecutionResourceBudget(
    string Schema, string Status, int ConfiguredParallelism, int VisibleProcessors,
    int EffectiveParallelism, string SchedulingMode,
    long? HostAvailableMemoryBytes, long? CgroupRemainingMemoryBytes,
    long? EffectiveAvailableMemoryBytes, long? FreeDiskBytes,
    long HostReserveBytes, long PerWorkerReserveBytes, long MinimumFreeDiskBytes,
    bool ResourceAdapted, bool TestUsed, bool GoldUsed, bool Activated);

public static class V9CalibrationResourceGovernor
{
    public const string Schema = "V9_CALIBRATOR_RUNTIME_RESOURCE_BUDGET_V1";
    public const long GiB = 1024L * 1024 * 1024;
    // Legacy receipt fields remain for audit compatibility, but there is no
    // fixed RAM reservation per worker. The OS/cgroup owns physical memory.
    public const long HostReserveBytes = 0;
    public const long PerWorkerReserveBytes = 0;
    public const long CriticalAvailableMemoryBytes = 1 * GiB;
    public const long PreventiveAvailableMemoryBytes = 2 * GiB;
    public const long MinimumFreeDiskBytes = 3 * GiB;

    /// <summary>
    /// Automatic mode uses max(1, visible CPUs - 1) as its concurrency ceiling.
    /// Workers do not pin or reserve an OS CPU. No per-worker RAM allocation is
    /// reserved: live OS/cgroup availability gates each additional admission.
    /// Explicit positive plans remain authoritative; unmeasured memory remains
    /// an auditable conservative one-worker fallback.
    /// </summary>
    public static V9CalibrationExecutionResourceBudget Decide(
        int configuredParallelism, int visibleProcessors,
        long? hostAvailableMemoryBytes, long? cgroupRemainingMemoryBytes,
        long? freeDiskBytes)
    {
        if (configuredParallelism < 0 || visibleProcessors < 1
            || hostAvailableMemoryBytes is < 0 || cgroupRemainingMemoryBytes is < 0
            || freeDiskBytes is < 0)
            throw new InvalidDataException("Amostra de recursos inválida para calibração V9.");
        if (freeDiskBytes is null || freeDiskBytes < MinimumFreeDiskBytes)
            throw new InvalidDataException("V9_RESOURCE_ADMISSION_DISK_INSUFFICIENT_OR_UNMEASURED");

        long? available = hostAvailableMemoryBytes.HasValue && cgroupRemainingMemoryBytes.HasValue
            ? Math.Min(hostAvailableMemoryBytes.Value, cgroupRemainingMemoryBytes.Value)
            : hostAvailableMemoryBytes ?? cgroupRemainingMemoryBytes;
        if (available.HasValue && available.Value < PreventiveAvailableMemoryBytes)
            throw new InvalidDataException("V9_RESOURCE_ADMISSION_RAM_BELOW_PREVENTIVE_FLOOR");

        int workers;
        string scheduling;
        if (configuredParallelism > 0)
        {
            // Respect explicitly declared reproducibility/concurrency choices.
            // Never silently replace the user's positive number with a fitted value.
            workers = configuredParallelism;
            scheduling = "EXPLICIT_PLAN_UNCHANGED";
        }
        else if (available.HasValue)
        {
            workers = Math.Max(1, visibleProcessors - 1);
            scheduling = "AUTO_CPU_MINUS_ONE_LIVE_RAM_ADMISSION";
        }
        else
        {
            workers = 1;
            scheduling = "AUTO_MEMORY_UNMEASURED_CONSERVATIVE_SINGLE_WORKER";
        }

        return new(Schema, "ADMITTED_RESOURCE_ESTIMATE_NOT_PHYSICAL_PROOF",
            configuredParallelism, visibleProcessors, workers, scheduling,
            hostAvailableMemoryBytes, cgroupRemainingMemoryBytes, available, freeDiskBytes,
            HostReserveBytes, PerWorkerReserveBytes, MinimumFreeDiskBytes,
            configuredParallelism == 0, false, false, false);
    }

    /// <summary>
    /// Called from --v9-calibrate and --v9-adaptive-study after their fresh
    /// evidence directory has been created, before opening expensive SQL work.
    /// </summary>
    public static V9CalibrationExecutionResourceBudget Capture(int configuredParallelism, string outputDirectory)
    {
        var sample = CaptureLiveResources(outputDirectory);
        return Decide(configuredParallelism, Environment.ProcessorCount,
            sample.HostAvailableMemoryBytes, sample.CgroupRemainingMemoryBytes,
            sample.FreeDiskBytes);
    }

    /// <summary>
    /// Sample live resources without repeating the initial 2 GiB admission gate.
    /// Once a calibration is running, SQL and the first worker legitimately
    /// consume the memory admitted at startup. Reapplying the startup reserve
    /// to every runtime sample incorrectly rejects healthy in-flight work.
    /// The runtime scheduler still checks disk and incremental worker RAM
    /// before admitting additional workers; it must not use Decide here.
    /// </summary>
    public static (long? HostAvailableMemoryBytes, long? CgroupRemainingMemoryBytes,
        long? EffectiveAvailableMemoryBytes, long FreeDiskBytes)
        CaptureLiveResources(string outputDirectory)
    {
        var full = Path.GetFullPath(outputDirectory);
        var volume = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(volume))
            throw new InvalidDataException("V9_RESOURCE_ADMISSION_DISK_ROOT_NOT_FOUND");
        long freeDisk;
        try { freeDisk = new DriveInfo(volume).AvailableFreeSpace; }
        catch (IOException)
        {
            throw new InvalidDataException("V9_RESOURCE_ADMISSION_DISK_UNMEASURED");
        }
        long? hostAvailable = TryHostAvailableMemory();
        long? cgroupAvailable = TryCgroupRemainingMemory();
        long? available = hostAvailable.HasValue && cgroupAvailable.HasValue
            ? Math.Min(hostAvailable.Value, cgroupAvailable.Value)
            : hostAvailable ?? cgroupAvailable;
        return (hostAvailable, cgroupAvailable, available, freeDisk);
    }

    // Linux MemAvailable and Windows GlobalMemoryStatusEx report current host
    // availability; neither is a hard cap. cgroup v2 is applied separately.
    private static long? TryHostAvailableMemory()
    {
        if (OperatingSystem.IsWindows())
        {
            var info = new WindowsMemoryStatus { Length = (uint)Marshal.SizeOf<WindowsMemoryStatus>() };
            if (!GlobalMemoryStatusEx(ref info))
                return null;
            return info.AvailablePhysical > long.MaxValue ? null : (long)info.AvailablePhysical;
        }
        return TryLinuxAvailableMemory();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct WindowsMemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref WindowsMemoryStatus info);

    private static long? TryLinuxAvailableMemory()
    {
        const string file = "/proc/meminfo";
        if (!File.Exists(file)) return null; // Windows/macOS: conservative auto worker.
        foreach (var line in File.ReadLines(file))
        {
            if (!line.StartsWith("MemAvailable:", StringComparison.Ordinal)) continue;
            var parts = line["MemAvailable:".Length..]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[1] != "kB"
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var kb)
                || kb > long.MaxValue / 1024)
                throw new InvalidDataException("V9_RESOURCE_ADMISSION_MEMINFO_INVALID");
            return kb * 1024;
        }
        return null;
    }

    private static long? ReadNonnegativeFile(string path)
    {
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path).Trim();
        if (text == "max") return null;
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number < 0)
            throw new InvalidDataException("V9_RESOURCE_ADMISSION_CGROUP_INVALID");
        return number;
    }

    private static long? TryCgroupRemainingMemory()
    {
        // On cgroup v2, read both limit and usage; a 'max' limit means unbounded.
        var cap = ReadNonnegativeFile("/sys/fs/cgroup/memory.max");
        if (!cap.HasValue) return null;
        var used = ReadNonnegativeFile("/sys/fs/cgroup/memory.current")
            ?? throw new InvalidDataException("V9_RESOURCE_ADMISSION_CGROUP_USAGE_MISSING");
        return Math.Max(0, cap.Value - used);
    }
}
