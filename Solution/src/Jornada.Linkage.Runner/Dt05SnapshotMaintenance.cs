using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Jornada.Linkage.Runner;

public sealed record Dt05SnapshotMaintenanceReport(
    DateTimeOffset ScannedAtUtc,
    int ManifestCount,
    int ReferencedObjectOccurrences,
    int UniqueReferencedObjectCount,
    long LogicalReferencedBytes,
    long UniqueReferencedBytes,
    long DeduplicatedBytes,
    int PhysicalObjectCount,
    long PhysicalObjectBytes,
    int OrphanObjectCount,
    long OrphanObjectBytes,
    int StaleTempCount,
    long StaleTempBytes,
    int DeletedOrphanObjectCount,
    int DeletedTempCount,
    long ElapsedMilliseconds);

/// <summary>
/// Auditoria e coleta segura de artefatos físicos DT-05.
/// Nunca remove manifestos, objetos referenciados nem ZIPs Bronze.
/// Qualquer manifesto inválido interrompe a coleta antes de exclusões.
/// </summary>
public sealed class Dt05SnapshotMaintenance(string bronzeRoot)
{
    public async Task<Dt05SnapshotMaintenanceReport> ScanAsync(
        TimeSpan minimumAge,
        bool deleteOrphans,
        DateTimeOffset? nowUtc = null,
        CancellationToken ct = default)
    {
        if (minimumAge < TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(minimumAge),
                "DT-05: janela mínima de segurança deve ser de ao menos 1 hora.");

        var started = Stopwatch.StartNew();
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var snapshotRoot = Path.GetFullPath(Path.Combine(bronzeRoot, "linkage-snapshots", "v1"));
        if (!Directory.Exists(snapshotRoot))
            return Empty(now, started.ElapsedMilliseconds);

        var referenced = new Dictionary<string, long>(StringComparer.Ordinal);
        long logicalBytes = 0;
        var occurrences = 0;
        var manifests = EnumerateManifestFiles(snapshotRoot).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        // Fase 1: validar toda a malha de manifestos/objetos. Se qualquer item falhar,
        // não há GC; isso evita transformar um manifesto corrompido em falso órfão.
        foreach (var manifestPath in manifests)
        {
            ct.ThrowIfCancellationRequested();
            var bytes = await File.ReadAllBytesAsync(manifestPath, ct);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"DT-05: manifesto inválido: {Relative(snapshotRoot, manifestPath)}.");

            if (!root.TryGetProperty("partitions", out var partitions))
                continue;
            if (partitions.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"DT-05: partitions inválido em {Relative(snapshotRoot, manifestPath)}.");

            foreach (var partition in partitions.EnumerateArray())
            {
                var relative = partition.GetProperty("path").GetString()
                    ?? throw new InvalidDataException("DT-05: partição sem path.");
                var expectedSha = partition.GetProperty("sha256").GetString()
                    ?? throw new InvalidDataException("DT-05: partição sem sha256.");
                var expectedBytes = partition.GetProperty("bytes").GetInt64();
                if (expectedBytes < 0 || !IsSha(expectedSha))
                    throw new InvalidDataException("DT-05: metadados de partição inválidos.");

                var objectPath = ResolveObject(snapshotRoot, relative, expectedSha);
                if (!File.Exists(objectPath))
                    throw new FileNotFoundException($"DT-05: objeto referenciado ausente: {relative}.", objectPath);

                var info = new FileInfo(objectPath);
                if (info.Length != expectedBytes)
                    throw new InvalidDataException($"DT-05: tamanho físico diverge para {relative}.");
                var actualSha = await HashFileAsync(objectPath, ct);
                if (!string.Equals(actualSha, expectedSha, StringComparison.Ordinal))
                    throw new InvalidDataException($"DT-05: SHA físico diverge para {relative}.");

                occurrences++;
                logicalBytes = checked(logicalBytes + expectedBytes);
                referenced[objectPath] = expectedBytes;
            }
        }

        var objectFiles = Directory.Exists(Path.Combine(snapshotRoot, "objects"))
            ? Directory.EnumerateFiles(Path.Combine(snapshotRoot, "objects"), "*", SearchOption.AllDirectories)
                .Where(IsContentAddressedObject)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();

        long physicalBytes = 0;
        long orphanBytes = 0;
        var orphans = new List<string>();
        foreach (var path in objectFiles)
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            physicalBytes = checked(physicalBytes + info.Length);
            if (!referenced.ContainsKey(path))
            {
                orphanBytes = checked(orphanBytes + info.Length);
                if (IsOlderThan(info.LastWriteTimeUtc, now, minimumAge))
                    orphans.Add(path);
            }
        }

        var tempFiles = EnumerateTempFiles(snapshotRoot).Distinct(StringComparer.Ordinal).ToArray();
        long staleTempBytes = 0;
        var staleTemps = new List<string>();
        foreach (var path in tempFiles)
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (!info.Exists || !IsOlderThan(info.LastWriteTimeUtc, now, minimumAge))
                continue;
            staleTemps.Add(path);
            staleTempBytes = checked(staleTempBytes + info.Length);
        }

        var deletedOrphans = 0;
        var deletedTemps = 0;
        if (deleteOrphans)
        {
            foreach (var path in orphans)
            {
                ct.ThrowIfCancellationRequested();
                File.Delete(path);
                deletedOrphans++;
            }
            foreach (var path in staleTemps)
            {
                ct.ThrowIfCancellationRequested();
                File.Delete(path);
                deletedTemps++;
            }
        }

        started.Stop();
        var uniqueReferencedBytes = referenced.Values.Sum();
        return new Dt05SnapshotMaintenanceReport(
            now,
            manifests.Length,
            occurrences,
            referenced.Count,
            logicalBytes,
            uniqueReferencedBytes,
            logicalBytes - uniqueReferencedBytes,
            objectFiles.Length,
            physicalBytes,
            objectFiles.Count(x => !referenced.ContainsKey(x)),
            orphanBytes,
            staleTemps.Count,
            staleTempBytes,
            deletedOrphans,
            deletedTemps,
            started.ElapsedMilliseconds);
    }

    private static Dt05SnapshotMaintenanceReport Empty(DateTimeOffset now, long elapsed) =>
        new(now,0,0,0,0,0,0,0,0,0,0,0,0,0,0,elapsed);

    private static IEnumerable<string> EnumerateManifestFiles(string root) =>
        Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(path => Path.GetDirectoryName(path)!
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                .Contains("manifests", StringComparer.Ordinal));

    private static IEnumerable<string> EnumerateTempFiles(string root)
    {
        var tmp = Path.Combine(root, "tmp");
        if (Directory.Exists(tmp))
            foreach (var path in Directory.EnumerateFiles(tmp, "*", SearchOption.AllDirectories))
                yield return path;
        foreach (var path in Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories))
            yield return path;
    }

    private static bool IsContentAddressedObject(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension is not ".json" and not ".parquet") return false;
        var name = Path.GetFileNameWithoutExtension(path);
        return IsSha(name);
    }

    private static string ResolveObject(string root, string relative, string sha)
    {
        if (Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal)
            || !relative.StartsWith("objects/", StringComparison.Ordinal))
            throw new InvalidDataException("DT-05: path de partição inválido.");
        var extension = Path.GetExtension(relative);
        if (extension is not ".json" and not ".parquet")
            throw new InvalidDataException("DT-05: extensão de objeto não suportada.");
        var expected = $"objects/{sha[..2]}/{sha}{extension}";
        if (!string.Equals(relative, expected, StringComparison.Ordinal))
            throw new InvalidDataException("DT-05: path content-addressed diverge do SHA.");

        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("DT-05: path de objeto escapou da raiz de snapshots.");
        return full;
    }

    private static bool IsOlderThan(DateTime lastWriteUtc, DateTimeOffset now, TimeSpan age) =>
        new DateTimeOffset(DateTime.SpecifyKind(lastWriteUtc, DateTimeKind.Utc)) <= now - age;

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), ct);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static bool IsSha(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
}

public static class Dt05SnapshotMaintenanceCommand
{
    public static bool IsRequested(string[] args) =>
        args.Any(x => string.Equals(x, "--dt05-snapshot-maintenance-report", StringComparison.OrdinalIgnoreCase));

    public static async Task ExecuteAsync(string[] args, IConfiguration configuration, IHostEnvironment environment, CancellationToken ct)
    {
        var reportPath = RequireValue(args, "--dt05-snapshot-maintenance-report");
        var gc = args.Any(x => string.Equals(x, "--dt05-snapshot-gc", StringComparison.OrdinalIgnoreCase));
        var ageText = OptionalValue(args, "--dt05-snapshot-min-age-hours") ?? "24";
        if (!double.TryParse(ageText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var ageHours)
            || ageHours < 1 || ageHours > 24 * 365)
            throw new ArgumentException("--dt05-snapshot-min-age-hours deve estar entre 1 e 8760.");

        if (gc && !environment.IsDevelopment() && !environment.IsEnvironment("Test"))
            throw new InvalidOperationException("DT-05: GC físico é permitido somente em Development/Test.");

        var configured = configuration["BronzeStorage:RootPath"];
        var bronzeRoot = string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(Path.Combine(environment.ContentRootPath, "data", "bronze"))
            : Path.GetFullPath(configured);

        var report = await new Dt05SnapshotMaintenance(bronzeRoot)
            .ScanAsync(TimeSpan.FromHours(ageHours), gc, ct: ct);

        var fullReport = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullReport)!);
        await File.WriteAllTextAsync(fullReport,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, ct);

        Console.WriteLine($"DT-05 snapshot maintenance: manifests={report.ManifestCount}; objects={report.PhysicalObjectCount}; " +
                          $"referenced={report.UniqueReferencedObjectCount}; orphans={report.OrphanObjectCount}; " +
                          $"staleTemp={report.StaleTempCount}; deletedObjects={report.DeletedOrphanObjectCount}; " +
                          $"deletedTemp={report.DeletedTempCount}; uniqueBytes={report.UniqueReferencedBytes}; " +
                          $"deduplicatedBytes={report.DeduplicatedBytes}; elapsedMs={report.ElapsedMilliseconds}");
        Console.WriteLine($"ARTEFATO: {fullReport}");
    }

    private static string RequireValue(string[] args, string name) =>
        OptionalValue(args, name) ?? throw new ArgumentException($"{name} exige valor.");

    private static string? OptionalValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}
