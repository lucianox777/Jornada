using System.Text;
using System.Text.Json;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9GoldHoldoutReplayIntegrityTests
{
    private const ulong Seed = 20261025001UL;
    private const string Generator = "SYNTHETIC_TEST_GENERATOR_V1";
    private static readonly Lazy<Fixture> Sample = new(Build);
    private static string Sha(int n) => n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);
    private sealed record Fixture(byte[] Manifest, byte[] Observations, byte[] People, byte[] Truth);

    private static Fixture Build()
    {
        var people = new StringBuilder("base_person_id,particao,nome,nome_mae,data_nascimento,sexo,cpf,cns,cns_scenario,evaluation_weight\n");
        var observations = new StringBuilder("observacao_id,base_person_id,particao,gestor,nome,nome_mae,data_nascimento,sexo,cpf,cns,corrupcoes,evaluation_weight\n");
        for (var i = 0; i < 30000; i++)
        {
            var id = $"P{i:D7}";
            people.Append(id).Append(",TRAIN,NOME,MAE,1990-01-01,F,,,,1\n");
            observations.Append(id).Append("-G0-0,").Append(id)
                .Append(",TRAIN,G0,NOME,MAE,1990-01-01,F,,,,,1\n");
        }
        var peopleBytes = Encoding.UTF8.GetBytes(people.ToString());
        var observationsBytes = Encoding.UTF8.GetBytes(observations.ToString());
        var truthBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            generator_version = Generator, ruleset_version = "RULES_V1",
            rng_version = "RNG_V1", population_profile = "DEMOGRAPHIC_PRIMARY_V1",
            error_profile = "clean", seed = Seed, people = 30000, observations = 30000,
            input_fingerprint_sha256 = Sha(5)
        });
        static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = 1, generator_version = Generator, ruleset_version = "RULES_V1",
            rng_version = "RNG_V1", population_profile = "DEMOGRAPHIC_PRIMARY_V1",
            error_profile = "clean", seed = Seed, input_fingerprint_sha256 = Sha(5),
            outputs = new[]
            {
                new { path = "pessoas_verdade.csv", sha256 = Hash(peopleBytes) },
                new { path = "observacoes.csv", sha256 = Hash(observationsBytes) },
                new { path = "gabarito.json", sha256 = Hash(truthBytes) }
            }
        });
        return new(manifestBytes, observationsBytes, peopleBytes, truthBytes);
    }

    private static V9GoldHoldoutReplayIntegrity.Evidence Verify(
        Fixture? original = null, Fixture? replay = null, ulong seed = Seed,
        string? trainSha = null)
    {
        var a = original ?? Sample.Value;
        var b = replay ?? a;
        return V9GoldHoldoutReplayIntegrity.Verify(
            a.Manifest, a.Observations, a.People, a.Truth,
            b.Manifest, b.Observations, b.People, b.Truth,
            seed, trainSha ?? Sha(9), Generator);
    }

    [Test]
    public void New_clean_30k_cohort_replays_all_bytes_without_certification()
    {
        var evidence = Verify();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(evidence.Seed, Is.EqualTo(Seed));
            Assert.That(evidence.People, Is.EqualTo(30000));
            Assert.That(evidence.Observations, Is.EqualTo(30000));
            Assert.That(evidence.ReplayBytesMatched, Is.True);
            Assert.That(evidence.CohortFingerprintSha256, Has.Length.EqualTo(64));
            Assert.That(evidence.IndependentlyCertified, Is.False);
            Assert.That(evidence.TestUsed, Is.False);
            Assert.That(evidence.OperationallyActivated, Is.False);
        }));
    }

    [Test]
    public void Rejects_different_replay_or_train_reuse_or_historical_seed()
    {
        var data = Sample.Value;
        var replay = data with { Observations = Encoding.UTF8.GetBytes("different") };
        Assert.Throws<InvalidDataException>((Action)(() => Verify(replay: replay)));
        var actualSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data.Observations));
        Assert.Throws<InvalidDataException>((Action)(() => Verify(trainSha: actualSha)));
        Assert.Throws<InvalidDataException>((Action)(() => Verify(seed: 42UL)));
    }

    [Test]
    public void Rejects_tampered_manifest_and_truth_population()
    {
        var data = Sample.Value;
        var actualSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data.Observations));
        var badManifest = data with
        {
            Manifest = Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(data.Manifest).Replace(actualSha, Sha(8), StringComparison.Ordinal))
        };
        Assert.Throws<InvalidDataException>((Action)(() => Verify(original: badManifest)));
        var badTruth = data with
        {
            Truth = Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(data.Truth).Replace(
                    "\"people\":30000", "\"people\":29999", StringComparison.Ordinal))
        };
        Assert.Throws<InvalidDataException>((Action)(() => Verify(original: badTruth)));
    }
}
