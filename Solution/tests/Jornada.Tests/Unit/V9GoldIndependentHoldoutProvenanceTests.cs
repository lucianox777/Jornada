using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9GoldIndependentHoldoutProvenanceTests
{
    private const string LocalDb =
        @"Server=(localdb)\JornadaV9Gold20261025_abcdef12;Database=JornadaHarness;Integrated Security=true;";
    private static string Sha(int n) => n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static GoldTrainThreeOrderProtocol.FrozenGoldValidationCandidate Candidate() => new(
        "GOLD_V6_FROZEN_KNEE_VALIDATION_CANDIDATE_V1", 0.05m,
        Sha(1), Sha(2), Sha(3), Sha(4), 20261025001UL,
        9240, 5003, 28, 5, 6, 0.996, false, false, false, false);

    private static async Task<string> FixtureAsync(ulong seed = 20261025001UL)
    {
        var root = Path.Combine(Path.GetTempPath(), "gold-holdout-provenance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var peopleBuilder = new StringBuilder(
            "base_person_id,particao,nome,nome_mae,data_nascimento,sexo,cpf,cns,cns_scenario,evaluation_weight\n");
        var observationBuilder = new StringBuilder(
            "observacao_id,base_person_id,particao,gestor,nome,nome_mae,data_nascimento,sexo,cpf,cns,corrupcoes,evaluation_weight\n");
        for (var i = 0; i < 30000; i++)
        {
            peopleBuilder.Append("P").Append(i).Append(",TRAIN,A,B,2000-01-01,F,,,,1\n");
            observationBuilder.Append("O").Append(i).Append(",P").Append(i)
                .Append(",TRAIN,G0,A,B,2000-01-01,F,,,,1\n");
        }
        var people = peopleBuilder.ToString();
        var observations = observationBuilder.ToString();
        var fingerprint = Sha(6);
        var truth = JsonSerializer.Serialize(new
        {
            seed, people = 30000, observations = 30000,
            population_profile = "DEMOGRAPHIC_PRIMARY_V1",
            error_profile = "clean", generator_version = "GEN",
            ruleset_version = "RULES", rng_version = "RNG",
            input_fingerprint_sha256 = fingerprint
        });
        await File.WriteAllTextAsync(Path.Combine(root, "observacoes.csv"), observations);
        await File.WriteAllTextAsync(Path.Combine(root, "pessoas_verdade.csv"), people);
        await File.WriteAllTextAsync(Path.Combine(root, "gabarito.json"), truth);
        var manifest = new
        {
            schema_version = 1, seed, population_profile = "DEMOGRAPHIC_PRIMARY_V1",
            error_profile = "clean", generator_version = "GEN",
            ruleset_version = "RULES", rng_version = "RNG",
            input_fingerprint_sha256 = fingerprint,
            outputs = new[]
            {
                new { path = "observacoes.csv", sha256 = Hash(observations) },
                new { path = "pessoas_verdade.csv", sha256 = Hash(people) },
                new { path = "gabarito.json", sha256 = Hash(truth) }
            }
        };
        await File.WriteAllTextAsync(Path.Combine(root, "generation-manifest.json"),
            JsonSerializer.Serialize(manifest));
        return root;
    }

    [Test]
    public async Task Generated_bytes_and_truth_provenance_produce_uncertified_six_trial_plan()
    {
        var root = await FixtureAsync();
        try
        {
            var plan = await V9GoldIndependentHoldoutProvenance.VerifyAsync(Candidate(), root, LocalDb);
            Assert.Multiple(() =>
            {
                Assert.That(plan.RequiredPhysicalTrials, Is.EqualTo(6));
                Assert.That(plan.CandidateLowerThreshold, Is.EqualTo(0.05m));
                Assert.That(plan.IndependentlyCertified, Is.False);
                Assert.That(plan.TestUsed, Is.False);
                Assert.That(plan.OperationallyActivated, Is.False);
            });
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task Tampered_csv_fails_before_sql()
    {
        var root = await FixtureAsync();
        try
        {
            await File.AppendAllTextAsync(Path.Combine(root, "observacoes.csv"), "O2,P2\n");
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await V9GoldIndependentHoldoutProvenance.VerifyAsync(Candidate(), root, LocalDb));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task Historical_or_wrong_seed_fails_before_sql()
    {
        var root = await FixtureAsync(42);
        try
        {
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await V9GoldIndependentHoldoutProvenance.VerifyAsync(Candidate(), root, LocalDb));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task Operational_database_is_rejected_before_reading_any_corpus()
    {
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await V9GoldIndependentHoldoutProvenance.VerifyAsync(
                Candidate(), "/does-not-exist", "Server=production;Database=Jornada;Integrated Security=true;"));
    }
}
