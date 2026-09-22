using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Jornada.Linkage.SyntheticCorpus;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SyntheticIngestionWaveTests
{
    private static readonly DateTimeOffset DayOne =
        DateTimeOffset.Parse("2026-09-22T09:00:00-03:00", System.Globalization.CultureInfo.InvariantCulture);

    [Test]
    public void Waves_reuse_source_code_but_rotate_delivery_ids_and_reveal_cpf()
    {
        var source = Fixture();
        var scenario = new SyntheticWaveScenario(
            WaveCount: 3, DelayedArrivalProbability: 0,
            CpfRevealProbability: 1, NameCorrectionProbability: 1,
            MotherCorrectionProbability: 1, BirthDateRecoveryProbability: 1);
        var waves = SyntheticIngestionWavePlanner.Plan(source, scenario);
        var first = Build(waves[0]);
        var second = Build(waves[1]);

        Assert.Multiple(() =>
        {
            Assert.That(waves[0].NewSourceCount, Is.EqualTo(3));
            Assert.That(waves[1].UpdatedSourceCount, Is.EqualTo(3));
            Assert.That(waves[1].CpfRevealedCount, Is.EqualTo(2));
            Assert.That(waves[1].BirthDateRecoveredCount, Is.EqualTo(1));
            Assert.That(waves[2].Generation.Observations, Is.Empty);
            Assert.That(first.ExcludedObservationCount, Is.EqualTo(1));
            Assert.That(second.ExcludedObservationCount, Is.Zero);
        });

        var firstSource = first.TruthRows.Single(x =>
            x.BasePersonId == "P-TRUTH-001" && x.SyntheticGestor == "G0");
        var secondSource = second.TruthRows.Single(x =>
            x.BasePersonId == "P-TRUTH-001" && x.SyntheticGestor == "G0");
        Assert.That(secondSource.OpaquePersonId, Is.EqualTo(firstSource.OpaquePersonId));

        var firstOtherGestor = first.TruthRows.Single(x =>
            x.BasePersonId == "P-TRUTH-001" && x.SyntheticGestor == "G1");
        Assert.That(firstOtherGestor.OpaquePersonId, Is.Not.EqualTo(firstSource.OpaquePersonId));

        var firstPayload = ReadPerson(first, firstSource);
        var secondPayload = ReadPerson(second, secondSource);
        Assert.Multiple(() =>
        {
            Assert.That(firstPayload.GetProperty("cpf").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(secondPayload.GetProperty("cpf").GetString(), Is.EqualTo("11144477735"));
            Assert.That(secondPayload.GetProperty("codigoPessoaOrigem").GetString(),
                Is.EqualTo(firstPayload.GetProperty("codigoPessoaOrigem").GetString()));
            Assert.That(secondPayload.GetProperty("idPessoaEntrega").GetString(),
                Is.Not.EqualTo(firstPayload.GetProperty("idPessoaEntrega").GetString()));
            Assert.That(secondPayload.GetProperty("idPessoaEntrega").GetString(),
                Is.Not.EqualTo(secondPayload.GetProperty("codigoPessoaOrigem").GetString()));
        });

        foreach (var package in first.Packages.Concat(second.Packages))
        {
            using var stream = new MemoryStream(package.Bytes);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            using var reader = new StreamReader(zip.GetEntry("pessoas.jsonl")!.Open(), Encoding.UTF8);
            var payload = reader.ReadToEnd();
            Assert.That(payload, Does.Not.Contain("P-TRUTH"));
            Assert.That(payload, Does.Not.Contain("OBS-TRUTH"));
        }
    }

    [Test]
    public void Waves_are_deterministic_and_fully_incremental()
    {
        var source = Fixture();
        var scenario = new SyntheticWaveScenario(DelayedArrivalProbability: 0,
            CpfRevealProbability: 1, NameCorrectionProbability: 1,
            MotherCorrectionProbability: 1, BirthDateRecoveryProbability: 1);
        var left = SyntheticIngestionWavePlanner.Plan(source, scenario);
        var right = SyntheticIngestionWavePlanner.Plan(source, scenario);
        for (var index = 0; index < left.Count; index++)
        {
            var a = Build(left[index]);
            var b = Build(right[index]);
            Assert.That(a.Packages.Select(x => (x.Sha256, x.FileName)),
                Is.EqualTo(b.Packages.Select(x => (x.Sha256, x.FileName))));
            for (var j = 0; j < a.Packages.Count; j++)
                Assert.That(a.Packages[j].Bytes, Is.EqualTo(b.Packages[j].Bytes));
        }
        Assert.That(Build(left[2]).Packages, Is.Empty);
    }

    [Test]
    public async Task Per_wave_materialization_writes_separate_verifiable_manifests_and_truth()
    {
        var waves = SyntheticIngestionWavePlanner.Plan(Fixture(),
            new SyntheticWaveScenario(WaveCount: 2, DelayedArrivalProbability: 0,
                CpfRevealProbability: 1, NameCorrectionProbability: 1,
                MotherCorrectionProbability: 1, BirthDateRecoveryProbability: 1));
        var root = Path.Combine(Path.GetTempPath(), "jornada-wave-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            for (var i = 0; i < waves.Count; i++)
            {
                var wave = waves[i];
                var result = Build(wave);
                var dir = Path.Combine(root, "wave-" + (i + 1).ToString("D2",
                    System.Globalization.CultureInfo.InvariantCulture));
                var options = new SyntheticIngestionBridgeOptions(4, DayOne.AddDays(i),
                    "unit-test-pseudonymization-key-32-bytes",
                    StableSourceIdentity: true, WaveNumber: i);
                var materialized = await SyntheticIngestionBridgeMaterializer.WriteAsync(
                    dir, result, options, new string('A', 64));
                using var manifest = JsonDocument.Parse(
                    await File.ReadAllTextAsync(materialized.ManifestPath));
                Assert.Multiple(() =>
                {
                    Assert.That(manifest.RootElement.GetProperty("bridgeVersion").GetString(),
                        Is.EqualTo(SyntheticIngestionBridge.WaveBridgeVersion));
                    Assert.That(manifest.RootElement.GetProperty("materializedObservationCount").GetInt32(),
                        Is.EqualTo(result.MaterializedObservationCount));
                    Assert.That(manifest.RootElement.GetProperty("truthSidecar")
                        .GetProperty("allowedForScoring").GetBoolean(), Is.False);
                    Assert.That(materialized.ManifestSha256.ToUpperInvariant(),
                        Is.EqualTo(Convert.ToHexString(
                            System.Security.Cryptography.SHA256.HashData(
                                File.ReadAllBytes(materialized.ManifestPath)))));
                });
                foreach (var package in materialized.PackagePaths)
                {
                    using var archive = ZipFile.OpenRead(package);
                    Assert.That(archive.Entries.Select(x => x.FullName),
                        Is.EquivalentTo(new[] { "manifest.json", "pessoas.jsonl", "registros.jsonl" }));
                }
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Same_source_twice_in_one_wave_fails_closed()
    {
        var source = Fixture();
        var duplicate = source with
        {
            Observations = source.Observations.Take(2).ToArray()
        };
        var error = Assert.Throws<InvalidDataException>(() =>
            SyntheticIngestionBridge.Build(duplicate,
                new SyntheticIngestionBridgeOptions(4, DayOne,
                    "unit-test-pseudonymization-key-32-bytes",
                    StableSourceIdentity: true, WaveNumber: 0)));
        Assert.That(error!.Message, Does.Contain("Identificador-fonte repetido"));
    }

    [Test]
    public void Wave_number_and_rates_must_be_valid()
    {
        Assert.Throws<ArgumentException>(() =>
            new SyntheticIngestionBridgeOptions(4, DayOne,
                "unit-test-pseudonymization-key-32-bytes",
                StableSourceIdentity: true).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SyntheticWaveScenario(CpfRevealProbability: double.NaN).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SyntheticWaveScenario(WaveCount: 1).Validate());
    }

    private static SyntheticIngestionBridgeResult Build(SyntheticIngestionWave wave)
        => SyntheticIngestionBridge.Build(wave.Generation,
            new SyntheticIngestionBridgeOptions(4, DayOne.AddDays(wave.WaveNumber),
                "unit-test-pseudonymization-key-32-bytes",
                StableSourceIdentity: true, WaveNumber: wave.WaveNumber));

    private static JsonElement ReadPerson(
        SyntheticIngestionBridgeResult result, SyntheticIngestionTruthRow truth)
    {
        var package = result.Packages.Single(x => x.FileName == truth.PackageFileName);
        using var stream = new MemoryStream(package.Bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("pessoas.jsonl")!.Open(), Encoding.UTF8);
        foreach (var line in reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.GetProperty("codigoPessoaOrigem").GetString() == truth.OpaquePersonId)
                return doc.RootElement.Clone();
        }
        throw new AssertionException("Pessoa não encontrada no ZIP da onda.");
    }

    private static SyntheticCorpusGeneration Fixture()
    {
        var people = new[]
        {
            new SyntheticPerson { BasePersonId = "P-TRUTH-001", Partition = "TEST",
                Name = "Maria Silva", MotherName = "Ana Silva",
                BirthDate = new DateOnly(1980, 1, 2), Sex = "F", Cpf = "11144477735",
                EvaluationWeight = 1 },
            new SyntheticPerson { BasePersonId = "P-TRUTH-002", Partition = "TEST",
                Name = "Carlos Souza", MotherName = "Joana Souza",
                BirthDate = new DateOnly(1970, 2, 3), Sex = "M", EvaluationWeight = 1 }
        };
        SyntheticObservation Observation(string id, string person, string gestor,
            string name, string? mother, DateOnly? birth, string sex) => new()
        {
            ObservationId = id, BasePersonId = person, Partition = "TEST",
            Gestor = gestor, Name = name, MotherName = mother, BirthDate = birth,
            Sex = sex, Cpf = null, EvaluationWeight = 1, Corruptions = ""
        };
        var observations = new[]
        {
            Observation("OBS-TRUTH-001", "P-TRUTH-001", "G0", "Maria Silba",
                "Ana Silba", new DateOnly(1980, 1, 2), "F"),
            Observation("OBS-TRUTH-002", "P-TRUTH-001", "G0", "Maria Silva",
                "Ana Silva", new DateOnly(1980, 1, 2), "F"),
            Observation("OBS-TRUTH-003", "P-TRUTH-001", "G1", "Maria Silba",
                "Ana Silba", new DateOnly(1980, 1, 2), "F"),
            Observation("OBS-TRUTH-004", "P-TRUTH-002", "G0", "Carlos Souza",
                "Joana Souza", null, "M")
        };
        return new SyntheticCorpusGeneration(
            people, observations, SyntheticCorpusGenerator.ComputeEmpiricalM(observations),
            new SyntheticCorpusOptions(People: 2, Seed: 42));
    }
}
