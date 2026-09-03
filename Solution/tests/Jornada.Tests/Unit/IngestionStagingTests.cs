using Jornada.Api;

namespace Jornada.Tests.Unit;

public sealed class IngestionStagingTests
{
    [Test]
    public async Task Staging_receives_hashes_and_deletes_on_happy_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "jornada-staging-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new IngestionStagingStore(root);
            var bytes = "zip-sintetico"u8.ToArray();
            await using var input = new MemoryStream(bytes, writable: false);
            var staged = await store.ReceiveAsync(input, 1024, CancellationToken.None);
            Assert.That(File.Exists(staged.Path), Is.True);
            Assert.That(staged.Length, Is.EqualTo(bytes.Length));
            store.TryDelete(staged.Path);
            Assert.That(File.Exists(staged.Path), Is.False);
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Test]
    public void Cleanup_removes_only_old_staging_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "jornada-staging-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new IngestionStagingStore(root);
            var old = Path.Combine(root, "jornada-20200101000000-a.zip.part");
            var recent = Path.Combine(root, "jornada-20990101000000-b.zip.part");
            File.WriteAllText(old, "x"); File.WriteAllText(recent, "x");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-10));
            File.SetLastWriteTimeUtc(recent, DateTime.UtcNow);
            var removed = store.DeleteOlderThan(DateTimeOffset.UtcNow.AddHours(-6), CancellationToken.None);
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(File.Exists(recent), Is.True);
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }
}
