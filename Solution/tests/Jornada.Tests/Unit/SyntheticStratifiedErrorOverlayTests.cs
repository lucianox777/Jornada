using System.Text.Json;
using Jornada.Linkage.SyntheticCorpus;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SyntheticStratifiedErrorOverlayTests
{
    [Test]
    public void Config_rejects_invalid_rates_keys_and_versions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SyntheticStratifiedErrorRates { MissingMother = double.NaN }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SyntheticStratifiedErrorRates { DateCorruption = 1.01 }.Validate());
        Assert.Throws<ArgumentException>(
            () => new SyntheticStratifiedErrorConfig { Version = "NOPE" }.Validate(4));
        Assert.Throws<ArgumentException>(
            () => new SyntheticStratifiedErrorConfig
            {
                ByGestorAndCpfStratum = new() { ["G5/WITH_CPF"] = new() }
            }.Validate(4));
        Assert.Throws<ArgumentException>(
            () => new SyntheticStratifiedErrorConfig
            {
                ByCpfStratum = new() { ["UNKNOWN"] = new() }
            }.Validate(4));
    }

    [Test]
    public void Overrides_are_integral_and_priority_is_combined_then_gestor_then_cpf_then_default()
    {
        var c = new SyntheticStratifiedErrorConfig
        {
            Default = new() { NameCorruption = .1 },
            ByCpfStratum = new() { ["WITHOUT_CPF"] = new() { MissingMother = .2 } },
            ByGestor = new() { ["G0"] = new() { DateCorruption = .3 } },
            ByGestorAndCpfStratum = new()
            {
                ["G0/WITHOUT_CPF"] = new() { MissingDate = .4 }
            }
        };
        c.Validate(2);
        Assert.Multiple(() =>
        {
            Assert.That(c.Resolve("G0", false).MissingDate, Is.EqualTo(.4));
            Assert.That(c.Resolve("G0", false).DateCorruption, Is.Zero);
            Assert.That(c.Resolve("G0", true).DateCorruption, Is.EqualTo(.3));
            Assert.That(c.Resolve("G1", false).MissingMother, Is.EqualTo(.2));
            Assert.That(c.Resolve("G1", true).NameCorruption, Is.EqualTo(.1));
        });
        var reordered = new SyntheticStratifiedErrorConfig
        {
            Default = c.Default,
            ByGestorAndCpfStratum = c.ByGestorAndCpfStratum,
            ByGestor = c.ByGestor,
            ByCpfStratum = c.ByCpfStratum
        };
        Assert.That(reordered.ConfigSha256(), Is.EqualTo(c.ConfigSha256()));
    }

    [Test]
    public void Without_observed_cpf_gets_extra_missing_mother_and_date_after_retention()
    {
        var rates = new SyntheticStratifiedErrorConfig
        {
            ByCpfStratum = new()
            {
                ["WITHOUT_CPF"] = new() { MissingMother = 1, MissingDate = 1 },
                ["WITH_CPF"] = new()
            }
        };
        var without = Observation(null);
        var with = Observation("12345678909");
        SyntheticStratifiedErrorOverlay.Apply(without, new Xoshiro256StarStar(4), rates);
        SyntheticStratifiedErrorOverlay.Apply(with, new Xoshiro256StarStar(4), rates);
        Assert.Multiple(() =>
        {
            Assert.That(without.MotherName, Is.Null);
            Assert.That(without.BirthDate, Is.Null);
            Assert.That(without.Corruptions, Does.Contain("MAE_STRAT_MISSING"));
            Assert.That(without.Corruptions, Does.Contain("DATE_STRAT_MISSING"));
            Assert.That(with.MotherName, Is.EqualTo("MARIA SILVA"));
            Assert.That(with.BirthDate, Is.EqualTo(new DateOnly(1980, 7, 27)));
            Assert.That(with.Corruptions, Is.Empty);
        });
    }

    [Test]
    public void Generated_corpus_preserves_legacy_path_and_is_deterministic_in_stratified_mode()
    {
        var config = new SyntheticStratifiedErrorConfig
        {
            ByCpfStratum = new()
            {
                ["WITHOUT_CPF"] = new() { MissingMother = 1 },
                ["WITH_CPF"] = new()
            }
        };
        var generator = Generator();
        var original = new SyntheticCorpusOptions(
            People: 300, Seed: 101, ErrorProfile: "clean", CpfBasePrevalence: 1,
            CpfObservationRetention: .5);
        var baseline = generator.Generate(original);
        var baselineAgain = generator.Generate(original with { StratifiedErrors = null });
        var experimental = generator.Generate(original with { StratifiedErrors = config });
        var repeat = generator.Generate(original with { StratifiedErrors = config });
        Assert.Multiple(() =>
        {
            Assert.That(baseline.Observations.Select(x => (x.Cpf, x.Name, x.MotherName, x.Corruptions)),
                Is.EqualTo(baselineAgain.Observations.Select(x => (x.Cpf, x.Name, x.MotherName, x.Corruptions))));
            Assert.That(experimental.Observations.Select(x => (x.Cpf, x.Name, x.MotherName, x.Corruptions)),
                Is.EqualTo(repeat.Observations.Select(x => (x.Cpf, x.Name, x.MotherName, x.Corruptions))));
            Assert.That(experimental.Observations.Any(x => x.Cpf is null), Is.True);
            Assert.That(experimental.Observations.Any(x => x.Cpf is not null), Is.True);
            Assert.That(experimental.Observations.Where(x => x.Cpf is null)
                .All(x => x.MotherName is null), Is.True);
            Assert.That(experimental.Observations.Where(x => x.Cpf is not null)
                .All(x => !x.Corruptions.Contains("STRAT_", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public async Task Truth_and_manifest_record_explicit_config_and_realized_strata()
    {
        var config = new SyntheticStratifiedErrorConfig
        {
            ByCpfStratum = new() { ["WITHOUT_CPF"] = new() { MissingMother = 1 } }
        };
        var options = new SyntheticCorpusOptions(
            People: 40, Seed: 101, CpfBasePrevalence: 1, CpfObservationRetention: 0,
            StratifiedErrors: config);
        var first = new SyntheticFrequencySampler(
            [new SyntheticFrequencyValue("ANA", 100), new SyntheticFrequencyValue("JOAO", 80)], 1);
        var surnames = new SyntheticFrequencySampler(
            [new SyntheticFrequencyValue("SILVA", 100), new SyntheticFrequencyValue("LIMA", 80)], 1);
        var generation = new SyntheticCorpusGenerator(first, surnames).Generate(options);
        var source = new SyntheticCorpusNominalSource(first, surnames,
            "FIXTURE", "source.ndjson.gz", new string('A', 64), new string('B', 64), 2, 2, 2);
        var legacy = SyntheticCorpusInputIdentity.ComputeFingerprint(
            options.Seed, Array.Empty<IbgeProjectionFile>());
        var experimental = SyntheticCorpusInputIdentity.ComputeFingerprint(
            options.Seed, Array.Empty<IbgeProjectionFile>(), null, config);
        Assert.That(experimental, Is.Not.EqualTo(legacy));
        var root = Path.Combine(Path.GetTempPath(), "jornada-strata-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = await SyntheticCorpusMaterializer.WriteAsync(
                root, generation, source, experimental);
            using var truth = JsonDocument.Parse(await File.ReadAllTextAsync(paths.TruthPath));
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(paths.ManifestPath));
            Assert.Multiple(() =>
            {
                Assert.That(manifest.RootElement.GetProperty("stratified_error_overlay")
                    .GetProperty("config_sha256").GetString(), Is.EqualTo(config.ConfigSha256()));
                Assert.That(truth.RootElement.GetProperty("stratified_error_overlay_sha256").GetString(),
                    Is.EqualTo(config.ConfigSha256()));
                Assert.That(truth.RootElement.GetProperty("stratified_error_realized_label_counts")
                    .EnumerateObject().Any(), Is.True);
                Assert.That(truth.RootElement.GetProperty("stratified_error_stratum_basis").GetString(),
                    Is.EqualTo("observed_cpf_after_retention"));
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static SyntheticCorpusGenerator Generator() => new(
        new SyntheticFrequencySampler(
            [new SyntheticFrequencyValue("ANA", 100), new SyntheticFrequencyValue("JOAO", 80)], 1),
        new SyntheticFrequencySampler(
            [new SyntheticFrequencyValue("SILVA", 100), new SyntheticFrequencyValue("LIMA", 80)], 1));

    private static SyntheticObservation Observation(string? cpf) => new()
    {
        ObservationId = "O1",
        BasePersonId = "P1",
        Partition = "TEST",
        Gestor = "G0",
        Name = "ANA SILVA",
        MotherName = "MARIA SILVA",
        BirthDate = new DateOnly(1980, 7, 27),
        Sex = "F",
        Cpf = cpf,
        EvaluationWeight = 1,
        Corruptions = string.Empty
    };
}
