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

            string physical;
            await using (var physicalStream = File.OpenRead(path))
            {
                physical = Convert.ToHexString(await SHA256.HashDataAsync(physicalStream));
            }
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
        var source = string.Join("\\n", Directory.EnumerateFiles(sourceDir, "*.cs").Select(File.ReadAllText));
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

    [Test]
    public void Weighted_sampler_is_independent_of_input_order_for_same_seed()
    {
        var canonical = new[]
        {
            new WeightedValue<string>("A", "ANA", 1),
            new WeightedValue<string>("B", "BEATRIZ", 3),
            new WeightedValue<string>("C", "CARLA", 6)
        };
        var reordered = new[] { canonical[2], canonical[0], canonical[1] };

        var left = new DeterministicWeightedSampler<string>(canonical);
        var right = new DeterministicWeightedSampler<string>(reordered);
        var leftRandom = new Xoshiro256StarStar(42);
        var rightRandom = new Xoshiro256StarStar(42);

        var leftSequence = Enumerable.Range(0, 512).Select(_ => left.Next(leftRandom)).ToArray();
        var rightSequence = Enumerable.Range(0, 512).Select(_ => right.Next(rightRandom)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(left.TotalWeight, Is.EqualTo(10));
            Assert.That(right.TotalWeight, Is.EqualTo(10));
            Assert.That(leftSequence, Is.EqualTo(rightSequence));
        });
    }

    [Test]
    public void Weighted_sampler_tracks_declared_integer_distribution()
    {
        var sampler = new DeterministicWeightedSampler<string>(new[]
        {
            new WeightedValue<string>("A", "A", 1),
            new WeightedValue<string>("B", "B", 3),
            new WeightedValue<string>("C", "C", 6)
        });
        var random = new Xoshiro256StarStar(355);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["A"] = 0,
            ["B"] = 0,
            ["C"] = 0
        };

        const int draws = 20_000;
        for (var i = 0; i < draws; i++)
            counts[sampler.Next(random)]++;

        Assert.Multiple(() =>
        {
            Assert.That(counts["A"] / (double)draws, Is.InRange(0.08, 0.12));
            Assert.That(counts["B"] / (double)draws, Is.InRange(0.28, 0.32));
            Assert.That(counts["C"] / (double)draws, Is.InRange(0.58, 0.62));
        });
    }

    [Test]
    public void Weighted_sampler_rejects_zero_duplicate_key_and_overflow()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() =>
                new DeterministicWeightedSampler<string>(new[] { new WeightedValue<string>("A", "A", 0) }));
            Assert.Throws<ArgumentException>(() =>
                new DeterministicWeightedSampler<string>(new[]
                {
                    new WeightedValue<string>("A", "A", 1),
                    new WeightedValue<string>("A", "B", 1)
                }));
            Assert.Throws<ArgumentException>(() =>
                new DeterministicWeightedSampler<string>(new[]
                {
                    new WeightedValue<string>("A", "A", ulong.MaxValue),
                    new WeightedValue<string>("B", "B", 1)
                }));
        });
    }

    [Test]
    public void Sex_period_inspector_prefers_observed_joint_cells_when_present()
    {
        var rows = new[]
        {
            Row("NOME", "ANA", 100, "FEMININO", "1980-1989"),
            Row("NOME", "JOAO", 90, "MASCULINO", "1980-1989"),
            Row("NOME", "ANA", 200, "FEMININO", "TODOS"),
            Row("NOME", "ANA", 150, "TODOS", "1980-1989")
        };

        var plan = SexPeriodCompositionInspector.Inspect(rows);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Kind, Is.EqualTo(SexPeriodCompositionKind.ObservedJoint));
            Assert.That(plan.MethodVersion, Is.EqualTo(SexPeriodCompositionInspector.ObservedJointVersion));
            Assert.That(plan.JointCellCount, Is.EqualTo(2));
            Assert.That(plan.Declaration, Does.Contain("diretamente"));
        });
    }

    [Test]
    public void Sex_period_inspector_declares_independent_marginals_without_joint_cells()
    {
        var rows = new[]
        {
            Row("NOME", "ANA", 200, "FEMININO", "TODOS"),
            Row("NOME", "JOAO", 190, "MASCULINO", "TODOS"),
            Row("NOME", "ANA", 150, "TODOS", "1980-1989"),
            Row("NOME", "ANA", 120, "TODOS", "1990-1999")
        };

        var plan = SexPeriodCompositionInspector.Inspect(rows);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Kind, Is.EqualTo(SexPeriodCompositionKind.IndependentMarginals));
            Assert.That(plan.MethodVersion, Is.EqualTo(SexPeriodCompositionInspector.IndependentMarginalsVersion));
            Assert.That(plan.JointCellCount, Is.Zero);
            Assert.That(plan.SexMarginalCellCount, Is.EqualTo(2));
            Assert.That(plan.PeriodMarginalCellCount, Is.EqualTo(2));
            Assert.That(plan.Declaration, Does.Contain("não representa distribuição conjunta observada"));
        });
    }

    [Test]
    public void Sex_period_inspector_fails_closed_when_dimensions_are_incomplete()
    {
        var rows = new[]
        {
            Row("NOME", "ANA", 200, "FEMININO", "TODOS")
        };

        Assert.Throws<InvalidDataException>(() => SexPeriodCompositionInspector.Inspect(rows));
    }

    private static IbgeFrequencyRow Row(
        string tipo,
        string valor,
        long frequencia,
        string sexo,
        string periodo)
        => new(tipo, valor, frequencia, sexo, periodo, "BRASIL", "00", "0000000");

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
