using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

/// <summary>
/// DT-05 reproducible synthetic physical-snapshot cost/latency baseline.
/// No NAS, IBGE, real Bronze/SQL or user-provided root is ever opened.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class Dt05SyntheticSnapshotCostEvidenceTests
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    [Test]
    public async Task Reused_partitions_report_measured_physical_bytes_and_latency_without_gc()
    {
        const int uniquePartitions = 64;
        const int manifests = 8;
        // Unique random temp root; never infer paths from any real config.
        var root = Path.Combine(Path.GetTempPath(),
            "jornada-dt05-synthetic-cost-" + Guid.NewGuid().ToString("N"));
        try
        {
            var snapshot = Path.Combine(root, "linkage-snapshots", "v1");
            var pieces = new List<(string Relative, string Sha, long Bytes)>();
            for (var i = 0; i < uniquePartitions; i++)
            {
                var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                {
                    schema_version = "JORNADA_DT05_COST_FIXTURE_V1",
                    synthetic_index = i,
                    synthetic_data = new string((char)('a' + i % 26), 1024)
                }));
                var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
                var rel = $"objects/{sha[..2]}/{sha}.json";
                var path = Path.Combine(snapshot, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, payload);
                pieces.Add((rel, sha, payload.LongLength));
            }

            for (var wave = 0; wave < manifests; wave++)
            {
                var manifestPath = Path.Combine(snapshot, "candidate-state", "manifests",
                    $"synthetic-wave-{wave}.json");
                Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
                await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
                {
                    schema_version = "JORNADA_DT05_COST_FIXTURE_V1",
                    partitions = pieces.Select(p => new
                    {
                        path = p.Relative, sha256 = p.Sha, bytes = p.Bytes, rows = 1
                    }).ToArray()
                }));
            }

            var now = DateTimeOffset.UtcNow;
            var orphanPayload = Encoding.UTF8.GetBytes("JORNADA_DT05_SYNTHETIC_UNREFERENCED_ONLY");
            var orphanSha = Convert.ToHexString(SHA256.HashData(orphanPayload)).ToLowerInvariant();
            var orphan = Path.Combine(snapshot, "objects", orphanSha[..2], orphanSha + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(orphan)!);
            await File.WriteAllBytesAsync(orphan, orphanPayload);
            File.SetLastWriteTimeUtc(orphan, now.UtcDateTime.AddDays(-2));

            // Physical and logical cost are measured by the real scanner,
            // not estimated from a hardcoded compression/deduplication ratio.
            var result = await new Dt05SnapshotMaintenance(root).ScanAsync(
                TimeSpan.FromHours(24), deleteOrphans: false, nowUtc: now);

            var expectedUniqueBytes = pieces.Sum(p => p.Bytes);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(result.ManifestCount, Is.EqualTo(manifests));
                Assert.That(result.ReferencedObjectOccurrences, Is.EqualTo(uniquePartitions * manifests));
                Assert.That(result.UniqueReferencedObjectCount, Is.EqualTo(uniquePartitions));
                Assert.That(result.LogicalReferencedBytes, Is.EqualTo(expectedUniqueBytes * manifests));
                Assert.That(result.UniqueReferencedBytes, Is.EqualTo(expectedUniqueBytes));
                Assert.That(result.DeduplicatedBytes, Is.EqualTo(expectedUniqueBytes * (manifests - 1)));
                Assert.That(result.PhysicalObjectCount, Is.EqualTo(uniquePartitions + 1));
                Assert.That(result.OrphanObjectCount, Is.EqualTo(1));
                Assert.That(result.DeletedOrphanObjectCount, Is.Zero);
                Assert.That(result.DeletedTempCount, Is.Zero);
                Assert.That(result.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(0));
                Assert.That(File.Exists(orphan), Is.True, "Read-only evidence must not delete a file.");
            }));

            var reduction = result.LogicalReferencedBytes == 0 ? 0m
                : 1m - (decimal)result.UniqueReferencedBytes / result.LogicalReferencedBytes;
            var summary = new
            {
                schema_version = "JORNADA_DT05_SYNTHETIC_COST_V1",
                status = "PASS",
                scope = "SYNTHETIC_EPHEMERAL_READ_ONLY",
                git_sha = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "LOCAL_UNMEASURED",
                manifest_count = result.ManifestCount,
                occurrences = result.ReferencedObjectOccurrences,
                unique_objects = result.UniqueReferencedObjectCount,
                logical_bytes = result.LogicalReferencedBytes,
                unique_bytes = result.UniqueReferencedBytes,
                physical_bytes = result.PhysicalObjectBytes,
                deduplicated_bytes = result.DeduplicatedBytes,
                deduplication_fraction = reduction,
                scanned_elapsed_ms = result.ElapsedMilliseconds,
                orphan_count = result.OrphanObjectCount,
                deleted_objects = result.DeletedOrphanObjectCount,
                performance_threshold_enforced = false,
                real_nas_measurement = false,
                real_cpf_or_ibge_data = false
            };
            var json = JsonSerializer.Serialize(summary, PrettyJson);
            TestContext.WriteLine("DT-05 synthetic read-only physical cost: " + json);
            // Existing unit-test-evidence upload publishes this only inside
            // a GitHub-hosted CI runner; local test invocations only print it.
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true"
                && Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") == "lucianox777/Jornada"
                && Environment.GetEnvironmentVariable("CI") == "true")
            {
                var cursor = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                while (cursor is not null && cursor.Name != "Solution")
                    cursor = cursor.Parent;
                Assert.That(cursor, Is.Not.Null, "Solution source root not found in CI.");
                var path = Path.Combine(cursor!.FullName, ".local", "test-evidence",
                    "unit", "dt05-synthetic-cost.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, json + Environment.NewLine);
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true); // ONLY unique test-owned temp fixture
        }
    }
}
