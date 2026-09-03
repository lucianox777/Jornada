using Jornada.Api;
using Jornada.Contracts;
using Jornada.Ingestion;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class SchemaCacheTests
{
    [Test]
    public async Task Contract_catalog_discovers_new_version_without_restart()
    {
        var root = Path.Combine(Path.GetTempPath(), "jornada-contract-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var baseDir = Path.Combine(root, "config", "contracts", "gestores", "SMADS", "pessoa");
            Directory.CreateDirectory(Path.Combine(baseDir, "v1"));
            await File.WriteAllTextAsync(Path.Combine(baseDir, "v1", "pessoa.schema.json"), MinimalSchema("urn:test:v1"));

            var resolver = new FileSystemContractResolver(root);
            var context = new AccessContext(Guid.NewGuid(), AccessCredentialType.GESTOR, "SMADS", "SMADS", null, [], []);
            var first = await resolver.ResolvePersonSchemaAsync(context, CancellationToken.None);
            Assert.That(first, Does.Contain(Path.Combine("v1", "pessoa.schema.json")));

            Directory.CreateDirectory(Path.Combine(baseDir, "v2"));
            await File.WriteAllTextAsync(Path.Combine(baseDir, "v2", "pessoa.schema.json"), MinimalSchema("urn:test:v2"));
            var second = await resolver.ResolvePersonSchemaAsync(context, CancellationToken.None);
            Assert.That(second, Does.Contain(Path.Combine("v2", "pessoa.schema.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Validator_cache_rejects_in_place_change_of_loaded_version()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jornada-schema-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "pessoa.schema.json");
        try
        {
            File.WriteAllText(path, MinimalSchema("urn:test:v1"));
            var cache = new JsonSchemaValidatorCache();
            var first = cache.Get(path);
            Assert.That(first, Is.Not.Null);

            Thread.Sleep(1100); // garante mudança observável de LastWriteTime em FS com granularidade de 1 s.
            File.WriteAllText(path, MinimalSchema("urn:test:v1-alterada") + " ");
            Assert.That(() => cache.Get(path), Throws.TypeOf<InvalidOperationException>());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }


    [Test]
    public void Validator_cache_rejects_bytes_different_from_catalog_hash_even_after_restart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jornada-schema-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "pessoa.schema.json");
        try
        {
            File.WriteAllText(path, MinimalSchema("urn:test:approved"));
            var approved = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
            Assert.That(new JsonSchemaValidatorCache().Get(path, approved), Is.Not.Null);

            // Simula novo deploy/restart: cache novo, mesmo caminho v1, bytes adulterados.
            File.WriteAllText(path, MinimalSchema("urn:test:substituted"));
            Assert.That(() => new JsonSchemaValidatorCache().Get(path, approved), Throws.TypeOf<InvalidOperationException>());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void Decompressed_budget_is_shared_across_streams()
    {
        var budget = new DecompressedByteBudget(5);
        using var a = new DecompressedLimitStream(new MemoryStream(new byte[] {1,2,3}), budget);
        var buffer = new byte[3];
        Assert.That(a.Read(buffer, 0, buffer.Length), Is.EqualTo(3));

        using var b = new DecompressedLimitStream(new MemoryStream(new byte[] {4,5,6}), budget);
        Assert.That(() => b.Read(buffer, 0, buffer.Length), Throws.TypeOf<InvalidDataException>());
    }

    private static string MinimalSchema(string id) => $$"""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "$id": "{{id}}",
      "type": "object",
      "properties": {},
      "additionalProperties": false
    }
    """;
}
