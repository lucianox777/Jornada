using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9SyntheticObservationCapEvidenceTests
{
    private static (byte[] Bytes, V9SyntheticObservationCapDeclaration Declaration) Manifest(int maximum = 4,
        string scope = "BASE_CORPUS_ONLY_NOT_INGESTION_WAVES")
    {
        var csvSha = new string('a', 64);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            generator_version = SyntheticObservationMultiplicityContract.GeneratorVersion,
            observation_multiplicity = new
            {
                contract_version = SyntheticObservationMultiplicityContract.Version,
                maximum_observations_per_base_identity = maximum,
                support = SyntheticObservationMultiplicityContract.Counts,
                weights = SyntheticObservationMultiplicityContract.Weights,
                scope
            },
            outputs = new[] { new { path = "observacoes.csv", sha256 = csvSha } }
        });
        return (bytes, new(SyntheticObservationMultiplicityContract.Version,
            SyntheticObservationMultiplicityContract.GeneratorVersion, 4,
            Convert.ToHexString(SHA256.HashData(bytes)), csvSha));
    }

    [Test]
    public void Requires_pinned_generator_support_and_does_not_accept_observed_maximum_or_wave_scope()
    {
        var (bytes, declaration) = Manifest();
        Assert.DoesNotThrow((Action)(() => V9SyntheticObservationCapEvidence.ValidateManifest(
            bytes, declaration, declaration.ObservationsSha256, 4)));
        Assert.That((Action)(() => V9SyntheticObservationCapEvidence.ValidateManifest(
            bytes, declaration, new string('b', 64), 4)), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9SyntheticObservationCapEvidence.ValidateManifest(
            bytes, declaration with { ManifestSha256 = new string('c', 64) }, declaration.ObservationsSha256, 4)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9SyntheticObservationCapEvidence.ValidateDeclaration(
            declaration with { MaximumObservationsPerBaseIdentity = 2 }, 2)), Throws.TypeOf<InvalidDataException>());
        foreach (var invalid in new[] { Manifest(5), Manifest(scope: "INGESTION_WAVES") })
            Assert.That((Action)(() => V9SyntheticObservationCapEvidence.ValidateManifest(
                invalid.Bytes, invalid.Declaration, invalid.Declaration.ObservationsSha256, 4)),
                Throws.TypeOf<InvalidDataException>());
    }

    private static (byte[] ManifestBytes, byte[] TruthBytes) PinnedPopulation(string truthJson)
    {
        var truth = Encoding.UTF8.GetBytes(truthJson);
        var sha = Convert.ToHexString(SHA256.HashData(truth));
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            generator_version = SyntheticObservationMultiplicityContract.GeneratorVersion,
            seed = 42UL,
            outputs = new[]
            {
                new { path = "observacoes.csv", sha256 = new string('a', 64) },
                new { path = "gabarito.json", sha256 = sha }
            }
        });
        return (manifest, truth);
    }

    [TestCase(3)]
    [TestCase(20_000)]
    [TestCase(30_000)]
    [TestCase(45_000)]
    public void Pinned_population_comes_from_sha_authenticated_truth_aggregate_not_from_missing_manifest_field(int people)
    {
        var fixture = PinnedPopulation(JsonSerializer.Serialize(new
        {
            people, generator_version = SyntheticObservationMultiplicityContract.GeneratorVersion, seed = 42
        }));
        Assert.That(V9SyntheticObservationCapEvidence.ReadPinnedPopulationCount(
            fixture.ManifestBytes, fixture.TruthBytes), Is.EqualTo(people));
    }

    [Test]
    public void Pinned_population_fails_closed_on_bad_sha_or_malformed_count_and_origin()
    {
        foreach (var invalid in new[]
        {
            "{}", "{\"people\":0}", "{\"people\":-1}",
            "{\"people\":1.5}", "{\"people\":\"30000\"}", "{\"people\":null}",
            "{\"people\":30000,\"generator_version\":\"INVALID\",\"seed\":42}",
            "{\"people\":30000,\"generator_version\":\"JORNADA_SYNTH_CORPUS_CSHARP_V1\",\"seed\":43}"
        })
        {
            var fixture = PinnedPopulation(invalid);
            Assert.That((Action)(() => V9SyntheticObservationCapEvidence.ReadPinnedPopulationCount(
                    fixture.ManifestBytes, fixture.TruthBytes)), Throws.TypeOf<InvalidDataException>());
        }
        var valid = PinnedPopulation(JsonSerializer.Serialize(new
        {
            people = 30_000, generator_version = SyntheticObservationMultiplicityContract.GeneratorVersion, seed = 42
        }));
        Assert.That((Action)(() => V9SyntheticObservationCapEvidence.ReadPinnedPopulationCount(
            valid.ManifestBytes, Encoding.UTF8.GetBytes("{}"))), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9SyntheticObservationCapEvidence.ReadPinnedPopulationCount(
            Encoding.UTF8.GetBytes("{}"), valid.TruthBytes)), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Csv_cap_counts_cpf_observations_before_filtering_and_keeps_test_attributes_unread()
    {
        var path = Path.GetTempFileName();
        try
        {
            const string header = "base_person_id,particao,gestor,nome,nome_mae,data_nascimento,cpf\n";
            var training = string.Concat(Enumerable.Range(0, 4).Select(i => $"A,TRAIN,G{i},ANA SILVA,MARIA SILVA,1980-01-01,\n"));
            var validation = "B,VALIDATION,G0,JOAO LIMA,ANA LIMA,1980-01-01,\n";
            var test = "C,TEST,G0,NEVER USED,NEVER USED,INVALID-DATE,\n";
            File.WriteAllText(path, header + training + validation + test, Encoding.UTF8);
            var rows = V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(path, 4, 4);
            Assert.That(rows.Length, Is.EqualTo(5));
            Assert.That(rows.All(x => x.Partition != "TEST"), Is.True);
            File.AppendAllText(path, "A,TRAIN,G5,ANA SILVA,MARIA SILVA,1980-01-01,12345678901\n");
            Assert.That((Action)(() => V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(path, 4, 4)),
                Throws.TypeOf<InvalidDataException>());
        }
        finally { File.Delete(path); }
    }

    [TestCase(1)]
    [TestCase(36)]
    [TestCase(2493)]
    public void Population_cap_four_tightens_conditional_bound_without_certifying_independence(int comparisons)
    {
        // Deliberately constructed analytical fixture; not a reconstruction of
        // the physical identity outcomes or new statistical validation.
        var groups = Enumerable.Range(0, 5556).Select(i =>
            new V9IdentityClusterFdr.IdentityGroup("G" + i, i < 1949 ? 2 : 1, 0)).ToArray();
        var four = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatioByAnyErrorCp(groups, 4, comparisons);
        var sixteen = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatioByAnyErrorCp(groups, 16, comparisons);
        var alphaComponent = .05 / (2 * comparisons);
        var expected = 4 * (1 - Math.Pow(alphaComponent, 1d / 5556))
            / (7505d / 5556 - 4 * Math.Sqrt(Math.Log(1 / alphaComponent) / (2 * 5556)));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(four.UpperFdrConditional, Is.EqualTo(expected).Within(1e-12));
            Assert.That(four.UpperFdrConditional, Is.LessThan(sixteen.UpperFdrConditional!.Value));
            Assert.That(four.IidClusterAssumptionIndependentlyValidated, Is.False);
            Assert.That(four.OperationallyCertified, Is.False);
        }));
    }
}
