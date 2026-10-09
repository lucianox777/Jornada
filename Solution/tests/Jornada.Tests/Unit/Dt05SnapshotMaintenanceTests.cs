using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt05SnapshotMaintenanceTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dt05-maint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    [Test]
    public async Task Gc_preserves_referenced_and_fresh_files_and_removes_only_stale_unreachable_artifacts()
    {
        var now = new DateTimeOffset(2026, 10, 4, 23, 55, 0, TimeSpan.Zero);
        var shared = PublishObject("shared-payload");
        WriteManifest("candidate-state/manifests/a.json", shared);
        WriteManifest("blocking-projection/manifests/b.json", shared);

        var staleOrphan = PublishObject("stale-orphan");
        var freshOrphan = PublishObject("fresh-orphan");
        File.SetLastWriteTimeUtc(staleOrphan.Path, now.UtcDateTime.AddHours(-48));
        File.SetLastWriteTimeUtc(freshOrphan.Path, now.UtcDateTime.AddHours(-2));

        var temp = Path.Combine(SnapshotRoot(), "tmp", ".candidate-abandoned.parquet");
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        File.WriteAllText(temp, "partial");
        File.SetLastWriteTimeUtc(temp, now.UtcDateTime.AddHours(-48));

        var bronzeZip = Path.Combine(_root, "sha256", "aa", "bb", "keep.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(bronzeZip)!);
        File.WriteAllText(bronzeZip, "bronze");

        var report = await new Dt05SnapshotMaintenance(_root)
            .ScanAsync(TimeSpan.FromHours(24), deleteOrphans: true, now);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.ManifestCount, Is.EqualTo(2));
            Assert.That(report.ReferencedObjectOccurrences, Is.EqualTo(2));
            Assert.That(report.UniqueReferencedObjectCount, Is.EqualTo(1));
            Assert.That(report.LogicalReferencedBytes, Is.EqualTo(shared.Bytes * 2));
            Assert.That(report.UniqueReferencedBytes, Is.EqualTo(shared.Bytes));
            Assert.That(report.DeduplicatedBytes, Is.EqualTo(shared.Bytes));
            Assert.That(report.OrphanObjectCount, Is.EqualTo(2));
            Assert.That(report.DeletedOrphanObjectCount, Is.EqualTo(1));
            Assert.That(report.StaleTempCount, Is.EqualTo(1));
            Assert.That(report.DeletedTempCount, Is.EqualTo(1));
            Assert.That(File.Exists(shared.Path), Is.True);
            Assert.That(File.Exists(staleOrphan.Path), Is.False);
            Assert.That(File.Exists(freshOrphan.Path), Is.True);
            Assert.That(File.Exists(temp), Is.False);
            Assert.That(File.Exists(bronzeZip), Is.True, "GC DT-05 jamais pode atravessar para o namespace ZIP Bronze.");
        }));
    }

    [Test]
    public void Corrupt_manifest_fails_closed_before_any_gc()
    {
        var now = new DateTimeOffset(2026, 10, 4, 23, 55, 0, TimeSpan.Zero);
        var orphan = PublishObject("must-survive");
        File.SetLastWriteTimeUtc(orphan.Path, now.UtcDateTime.AddDays(-7));

        var manifests = Path.Combine(SnapshotRoot(), "candidate-state", "manifests");
        Directory.CreateDirectory(manifests);
        File.WriteAllText(Path.Combine(manifests, "broken.json"),
            """{"partitions":[{"path":"objects/aa/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.parquet","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","bytes":1}]}""");

        var maintenance = new Dt05SnapshotMaintenance(_root);
        Assert.ThrowsAsync<FileNotFoundException>((Func<Task>)(async () =>
            await maintenance.ScanAsync(TimeSpan.FromHours(24), true, now)));
        Assert.That(File.Exists(orphan.Path), Is.True,
            "A descoberta de corrupção deve interromper a coleta antes de excluir qualquer órfão.");
    }

    [Test]
    public async Task Read_only_scan_reports_orphans_without_mutation()
    {
        var now = DateTimeOffset.UtcNow;
        var orphan = PublishObject("audit-only");
        File.SetLastWriteTimeUtc(orphan.Path, now.UtcDateTime.AddDays(-2));

        var report = await new Dt05SnapshotMaintenance(_root)
            .ScanAsync(TimeSpan.FromHours(24), deleteOrphans: false, now);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.OrphanObjectCount, Is.EqualTo(1));
            Assert.That(report.DeletedOrphanObjectCount, Is.Zero);
            Assert.That(File.Exists(orphan.Path), Is.True);
        }));
    }

    private (string Path, string Relative, string Sha, long Bytes) PublishObject(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var relative = $"objects/{sha[..2]}/{sha}.json";
        var path = Path.Combine(SnapshotRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return (path, relative, sha, bytes.LongLength);
    }

    private void WriteManifest(string relativeManifest, (string Path, string Relative, string Sha, long Bytes) obj)
    {
        var path = Path.Combine(SnapshotRoot(), relativeManifest.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var document = new
        {
            schema_version = 99,
            partitions = new[] { new { path = obj.Relative, sha256 = obj.Sha, bytes = obj.Bytes, rows = 1 } }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(document));
    }

    private string SnapshotRoot() => Path.Combine(_root, "linkage-snapshots", "v1");
}
