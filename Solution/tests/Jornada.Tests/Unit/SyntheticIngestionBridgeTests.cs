using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Jornada.Ingestion;
using Jornada.Linkage.SyntheticCorpus;
using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SyntheticIngestionBridgeTests
{
    private static readonly DateTimeOffset ReferenceDate =
        DateTimeOffset.Parse("2026-09-21T00:00:00-03:00", System.Globalization.CultureInfo.InvariantCulture);

    [Test]
    public void Bridge_materializes_only_representable_observations_without_truth_leakage()
    {
        var generation = FixtureGeneration();
        var options = new SyntheticIngestionBridgeOptions(
            4,
            ReferenceDate,
            "unit-test-pseudonymization-key-32-bytes");

        var result = SyntheticIngestionBridge.Build(generation, options);

        Assert.Multiple(() =>
        {
            Assert.That(result.SourceObservationCount, Is.EqualTo(3));
            Assert.That(result.MaterializedObservationCount, Is.EqualTo(2));
            Assert.That(result.ExcludedObservationCount, Is.EqualTo(1));
            Assert.That(result.Packages, Has.Count.EqualTo(2));
            Assert.That(
                result.TruthRows.Single(x => x.Status == "EXCLUIDA_CONTRATO_ATIVO").ExclusionReason,
                Is.EqualTo(SyntheticIngestionBridge.MissingBirthDateReason));
        });

        foreach (var package in result.Packages)
        {
            var manifest = IngestionPackageInspector.ParseAndValidate(package.Bytes);
            Assert.That(manifest.PessoaSchemaVersao, Is.EqualTo(4));
            Assert.That(manifest.DataReferencia, Is.EqualTo(ReferenceDate));

            using var stream = new MemoryStream(package.Bytes, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var pessoas = ReadEntry(zip, "pessoas.jsonl");
            var registros = ReadEntry(zip, "registros.jsonl");

            Assert.Multiple(() =>
            {
                Assert.That(registros, Is.Empty);
                Assert.That(pessoas, Does.Not.Contain("P-TRUTH-001"));
                Assert.That(pessoas, Does.Not.Contain("P-TRUTH-002"));
                Assert.That(pessoas, Does.Not.Contain("OBS-TRUTH-A"));
                Assert.That(pessoas, Does.Not.Contain("OBS-TRUTH-B"));
                Assert.That(pessoas, Does.Contain("SYNTH-"));
            });

            ValidatePeopleAgainstGestorV4(package.GestorCodigo, pessoas);
        }
    }

    [Test]
    public void Bridge_uses_seeded_HMAC_pseudonyms_and_is_byte_deterministic()
    {
        var generation = FixtureGeneration();
        var options = new SyntheticIngestionBridgeOptions(
            4,
            ReferenceDate,
            "unit-test-pseudonymization-key-32-bytes");

        var left = SyntheticIngestionBridge.Build(generation, options);
        var right = SyntheticIngestionBridge.Build(generation, options);

        Assert.Multiple(() =>
        {
            Assert.That(
                left.Packages.Select(x => (x.FileName, x.Sha256, x.PeopleCount)),
                Is.EqualTo(right.Packages.Select(x => (x.FileName, x.Sha256, x.PeopleCount))));
            for (var i = 0; i < left.Packages.Count; i++)
                Assert.That(left.Packages[i].Bytes, Is.EqualTo(right.Packages[i].Bytes), $"package={i}");
            Assert.That(
                left.TruthRows.Select(x => x.OpaquePersonId),
                Is.EqualTo(right.TruthRows.Select(x => x.OpaquePersonId)));
            Assert.That(left.PseudonymizationKeySha256, Is.EqualTo(right.PseudonymizationKeySha256));
        });

        var other = SyntheticIngestionBridge.Build(
            generation,
            options with { PseudonymizationKey = "different-unit-test-pseudonymization-key" });

        Assert.That(
            left.TruthRows.Where(x => x.OpaquePersonId is not null).Select(x => x.OpaquePersonId),
            Is.Not.EqualTo(other.TruthRows.Where(x => x.OpaquePersonId is not null).Select(x => x.OpaquePersonId)));
        Assert.That(
            left.Packages.Select(x => x.Sha256),
            Is.Not.EqualTo(other.Packages.Select(x => x.Sha256)));
    }

    [Test]
    public void Default_routes_target_the_seeded_Development_systems()
    {
        Assert.That(
            SyntheticIngestionBridge.DefaultRoutes
                .Select(x => (x.SyntheticGestor, x.GestorCodigo, x.CodigoSistemaOrigem)),
            Is.EqualTo(new[]
            {
                ("G0", "SEHAB", "SEHAB"),
                ("G1", "SMADS", "ASSISTENCIA"),
                ("G2", "SMDET", "TRABALHO"),
                ("G3", "SMS", "SAUDE")
            }));
    }

    [Test]
    public async Task Bridge_materializer_keeps_truth_sidecar_outside_packages_and_is_stable()
    {
        var generation = FixtureGeneration();
        var options = new SyntheticIngestionBridgeOptions(
            4,
            ReferenceDate,
            "unit-test-pseudonymization-key-32-bytes");
        var result = SyntheticIngestionBridge.Build(generation, options);

        var root = Path.Combine(Path.GetTempPath(), "jornada-synthetic-bridge-" + Guid.NewGuid().ToString("N"));
        try
        {
            var left = await SyntheticIngestionBridgeMaterializer.WriteAsync(
                Path.Combine(root, "a"),
                result,
                options,
                new string('A', 64));
            var right = await SyntheticIngestionBridgeMaterializer.WriteAsync(
                Path.Combine(root, "b"),
                result,
                options,
                new string('A', 64));

            Assert.Multiple(() =>
            {
                Assert.That(await File.ReadAllBytesAsync(left.ManifestPath),
                    Is.EqualTo(await File.ReadAllBytesAsync(right.ManifestPath)));
                Assert.That(await File.ReadAllBytesAsync(left.TruthPath),
                    Is.EqualTo(await File.ReadAllBytesAsync(right.TruthPath)));
                Assert.That(left.ManifestSha256, Is.EqualTo(right.ManifestSha256));
                Assert.That(left.TruthSha256, Is.EqualTo(right.TruthSha256));
            });

            var truthText = await File.ReadAllTextAsync(left.TruthPath);
            var manifestText = await File.ReadAllTextAsync(left.ManifestPath);
            Assert.Multiple(() =>
            {
                Assert.That(truthText, Does.Contain("P-TRUTH-001"));
                Assert.That(manifestText, Does.Not.Contain("P-TRUTH-001"));
                Assert.That(manifestText, Does.Contain(""allowedForScoring": false"));
                Assert.That(manifestText, Does.Contain(SyntheticIngestionBridge.MissingBirthDateReason));
            });

            foreach (var packagePath in left.PackagePaths)
            {
                var bytes = await File.ReadAllBytesAsync(packagePath);
                var asLatin = Encoding.Latin1.GetString(bytes);
                Assert.That(asLatin, Does.Not.Contain("P-TRUTH-001"));
                Assert.That(asLatin, Does.Not.Contain("P-TRUTH-002"));
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Unknown_synthetic_gestor_fails_closed()
    {
        var source = FixtureGeneration();
        var first = source.Observations[0];
        var unknown = new SyntheticObservation
        {
            ObservationId = first.ObservationId,
            BasePersonId = first.BasePersonId,
            Partition = first.Partition,
            Gestor = "G9",
            Name = first.Name,
            MotherName = first.MotherName,
            BirthDate = first.BirthDate,
            Sex = first.Sex,
            Cpf = first.Cpf,
            Cns = first.Cns,
            EvaluationWeight = first.EvaluationWeight,
            Corruptions = first.Corruptions
        };
        var generation = new SyntheticCorpusGeneration(
            source.People,
            new[] { unknown }.Concat(source.Observations.Skip(1)).ToArray(),
            source.EmpiricalMExact,
            source.Options);

        var error = Assert.Throws<InvalidDataException>(() =>
            SyntheticIngestionBridge.Build(
                generation,
                new SyntheticIngestionBridgeOptions(
                    4,
                    ReferenceDate,
                    "unit-test-pseudonymization-key-32-bytes")));

        Assert.That(error!.Message, Does.Contain("Sem rota de ingestão"));
    }

    private static SyntheticCorpusGeneration FixtureGeneration()
    {
        var options = new SyntheticCorpusOptions(People: 2, Seed: 42, Gestores: 4);
        var observations = new List<SyntheticObservation>
        {
            new()
            {
                ObservationId = "OBS-TRUTH-A",
                BasePersonId = "P-TRUTH-001",
                Partition = "TRAIN",
                Gestor = "G0",
                Name = "Maria Silva",
                MotherName = "Ana Silva",
                BirthDate = new DateOnly(1980, 1, 2),
                Sex = "F",
                Cpf = "11144477735",
                Cns = "700000000000007",
                EvaluationWeight = 1,
                Corruptions = string.Empty
            },
            new()
            {
                ObservationId = "OBS-TRUTH-B",
                BasePersonId = "P-TRUTH-001",
                Partition = "TRAIN",
                Gestor = "G1",
                Name = "Maria Silva",
                MotherName = null,
                BirthDate = new DateOnly(1980, 1, 2),
                Sex = "F",
                Cpf = null,
                Cns = null,
                EvaluationWeight = 1,
                Corruptions = "MAE_MISSING"
            },
            new()
            {
                ObservationId = "OBS-TRUTH-C",
                BasePersonId = "P-TRUTH-002",
                Partition = "TEST",
                Gestor = "G0",
                Name = "Carlos Souza",
                MotherName = "Joana Souza",
                BirthDate = null,
                Sex = "M",
                Cpf = null,
                Cns = null,
                EvaluationWeight = 1,
                Corruptions = "DATE_MISSING"
            }
        };

        return new SyntheticCorpusGeneration(
            Array.Empty<SyntheticPerson>(),
            observations,
            SyntheticCorpusGenerator.ComputeEmpiricalM(observations),
            options);
    }

    private static void ValidatePeopleAgainstGestorV4(string gestor, string jsonl)
    {
        var root = FindRepositoryRoot();
        var schema = Path.Combine(
            root,
            "Solution",
            "config",
            "contracts",
            "gestores",
            gestor,
            "pessoa",
            "v4",
            "pessoa.schema.json");
        var validator = JsonSchemaSubsetValidator.Load(schema);
        var lines = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++)
            Assert.DoesNotThrow(() => validator.ParseAndValidate(lines[i], "pessoas.jsonl", i + 1));
    }

    private static string ReadEntry(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

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
