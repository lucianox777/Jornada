using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Jornada.Linkage.SyntheticCorpus;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SyntheticCorpusFoundationTests
{
    [Test]
    public void Xoshiro_seed_42_matches_frozen_vector()
    {
        var rng = new Xoshiro256StarStar(42);
        var expected = new[]
        {
            0x15780B2E0C2EC716UL,
            0x6104D9866D113A7EUL,
            0xAE17533239E499A1UL,
            0xECB8AD4703B360A1UL,
            0xFDE6DC7FE2EC5E64UL,
            0xC50DA53101795238UL,
            0xB82154855A65DDB2UL,
            0xD99A2743EBE60087UL
        };

        Assert.That(
            Enumerable.Range(0, expected.Length).Select(_ => rng.NextUInt64()).ToArray(),
            Is.EqualTo(expected));
        Assert.That(rng.InitialStateHex, Is.EqualTo(
            "BDD732262FEB6E95:28EFE333B266F103:47526757130F9F52:581CE1FF0E4AE394"));
    }

    [Test]
    public void Input_fingerprint_is_independent_of_manifest_order()
    {
        var a = FileMeta("a.ndjson.gz", "A", "11", "22", 10);
        var b = FileMeta("b.ndjson.gz", "B", "33", "44", 20);

        var left = SyntheticCorpusInputIdentity.ComputeFingerprint(42, new[] { a, b });
        var right = SyntheticCorpusInputIdentity.ComputeFingerprint(42, new[] { b, a });

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left, Has.Length.EqualTo(64));
    }

    [Test]
    public async Task Projection_reader_verifies_physical_and_canonical_hashes()
    {
        var root = Path.Combine(Path.GetTempPath(), "jornada-synthetic-corpus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "projection"));
        try
        {
            var lines = new[]
            {
                """{"tipo":"NOME","valor":"ANA","frequencia":10,"sexo":"FEMININO","periodoNascimento":"1980-1989","escopoGeografico":"BRASIL","ufCodigo":"00","municipioCodigo":"0000000"}""",
                """{"tipo":"SOBRENOME","valor":"SILVA","frequencia":20,"sexo":"TODOS","periodoNascimento":"TODOS","escopoGeografico":"BRASIL","ufCodigo":"00","municipioCodigo":"0000000"}"""
            };
            var path = Path.Combine(root, "projection", "fixture.ndjson.gz");
            await using (var file = File.Create(path))
            await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
            await using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
            {
                foreach (var line in lines)
                    await writer.WriteLineAsync(line);
            }

            var physical = Convert.ToHexString(await SHA256.HashDataAsync(File.OpenRead(path)));
            var canonicalText = string.Join("\n", lines) + "\n";
            var canonical = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText)));
            var meta = new IbgeProjectionFile(
                "projection/fixture.ndjson.gz",
                "TEST",
                true,
                physical,
                canonical,
                lines.Length);

            var result = await IbgeProjectionReader.ReadFilteredAsync(root, meta, row => row.Tipo == "NOME");

            Assert.Multiple(() =>
            {
                Assert.That(result.RowCount, Is.EqualTo(2));
                Assert.That(result.Rows, Has.Count.EqualTo(1));
                Assert.That(result.Rows[0].Valor, Is.EqualTo("ANA"));
                Assert.That(result.PhysicalSha256, Is.EqualTo(physical));
                Assert.That(result.CanonicalContentSha256, Is.EqualTo(canonical));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Synthetic_foundation_does_not_use_SystemRandom_or_reverse_dependency()
    {
        var root = FindRepositoryRoot();
        var sourceDir = Path.Combine(root, "Solution", "src", "Jornada.Linkage.SyntheticCorpus");
        var source = string.Join(
            "\
",
            Directory.EnumerateFiles(sourceDir, "*.cs").Select(File.ReadAllText));
        var workerProject = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Linkage.Parameters.Worker",
            "Jornada.Linkage.Parameters.Worker.csproj"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("System.Random"));
            Assert.That(source, Does.Not.Contain("new Random("));
            Assert.That(workerProject, Does.Not.Contain("Jornada.Linkage.SyntheticCorpus"));
        });
    }

    private static IbgeProjectionFile FileMeta(string path, string kind, string shaByte, string canonicalByte, long rows)
        => new(
            path,
            kind,
            true,
            string.Concat(Enumerable.Repeat(shaByte, 32)),
            string.Concat(Enumerable.Repeat(canonicalByte, 32)),
            rows);

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(current.FullName, "Solution")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Raiz do repositório Jornada não encontrada.");
    }
}
