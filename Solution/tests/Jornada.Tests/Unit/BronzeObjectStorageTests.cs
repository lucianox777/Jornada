using Jornada.Bronze.Storage;
using System.Security.Cryptography;

namespace Jornada.Tests.Unit;

public sealed class BronzeObjectStorageTests
{
    [Test]
    public async Task File_store_is_content_addressed_and_idempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), "jornada-bronze-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemBronzeObjectStore(root);
            var bytes = "PK synthetic zip"u8.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            await using var first = new MemoryStream(bytes, writable: false);
            var a = await store.PutIfAbsentAsync(hash, first, bytes.Length, CancellationToken.None);
            await using var second = new MemoryStream(bytes, writable: false);
            var b = await store.PutIfAbsentAsync(hash, second, bytes.Length, CancellationToken.None);
            Assert.That(a.ObjectKey, Is.EqualTo($"sha256/{hash[..2]}/{hash.Substring(2,2)}/{hash}.zip"));
            Assert.That(b.ObjectKey, Is.EqualTo(a.ObjectKey));
            await using var read = await store.OpenReadAsync(a.ObjectKey, CancellationToken.None);
            using var ms = new MemoryStream();
            await read.CopyToAsync(ms);
            Assert.That(ms.ToArray(), Is.EqualTo(bytes));
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Test]
    public void Object_key_rejects_path_escape()
    {
        var root = Path.Combine(Path.GetTempPath(), "jornada-bronze-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemBronzeObjectStore(root);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                await using var _ = await store.OpenReadAsync("../escape.zip", CancellationToken.None);
            });
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Test]
    public void Existing_object_with_same_length_but_wrong_hash_is_rejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "jornada-bronze-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemBronzeObjectStore(root);
            var expected = "PK canonical bytes"u8.ToArray();
            var corrupted = "PK corrupted bytes"u8.ToArray();
            Assert.That(corrupted.Length, Is.EqualTo(expected.Length), "Fixture deve preservar o mesmo tamanho para exercitar hash, não tamanho.");
            var hash = Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant();
            var key = store.BuildObjectKey(hash);
            var path = Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, corrupted);

            Assert.ThrowsAsync<BronzeObjectIntegrityException>(async () =>
            {
                await using var content = new MemoryStream(expected, writable: false);
                await store.PutIfAbsentAsync(hash, content, expected.Length, CancellationToken.None);
            });
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Test]
    public async Task Verify_detects_missing_and_corrupted_objects()
    {
        var root = Path.Combine(Path.GetTempPath(), "jornada-bronze-verify-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemBronzeObjectStore(root);
            var bytes = "verified bronze object"u8.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            await using (var source = new MemoryStream(bytes, writable: false))
                await store.PutIfAbsentAsync(hash, source, bytes.Length, CancellationToken.None);
            var key = store.BuildObjectKey(hash);

            Assert.DoesNotThrowAsync(async () => await store.VerifyAsync(key, hash, bytes.Length, CancellationToken.None));

            var path = Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(path, "corrupted bronze object"u8.ToArray());
            Assert.ThrowsAsync<BronzeObjectIntegrityException>(async () =>
                await store.VerifyAsync(key, hash, bytes.Length, CancellationToken.None));

            File.Delete(path);
            Assert.ThrowsAsync<BronzeObjectNotFoundException>(async () =>
                await store.VerifyAsync(key, hash, bytes.Length, CancellationToken.None));
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

}
