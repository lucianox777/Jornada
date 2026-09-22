using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.SyntheticCorpus;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SyntheticBrazilianNameErrorTests
{
    private static readonly SyntheticErrorProfile NoLegacyErrors = new(0, 0, 0, 0, 0, 0);

    [Test]
    public void Configuration_is_explicit_versioned_and_rejects_invalid_rates_or_strata()
    {
        Assert.DoesNotThrow(() => new SyntheticBrazilianNameErrorConfig().Validate(4));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SyntheticBrazilianNameErrorRates { DuplicateLetter = double.NaN }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SyntheticBrazilianNameErrorRates { DuplicateParticle = double.PositiveInfinity }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SyntheticBrazilianNameErrorRates { OmitAgnome = -0.1 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SyntheticBrazilianNameErrorRates { PrefixTitle = 1.1 }.Validate());
        Assert.Throws<ArgumentException>(
            () => new SyntheticBrazilianNameErrorConfig { Version = "UNKNOWN" }.Validate(4));
        Assert.Throws<ArgumentException>(
            () => new SyntheticBrazilianNameErrorConfig
            {
                ByGestor = new() { ["G4"] = new() }
            }.Validate(4));
        Assert.Throws<ArgumentException>(
            () => new SyntheticBrazilianNameErrorConfig
            {
                ByGestorAndCpfStratum = new() { ["G0/UNKNOWN"] = new() }
            }.Validate(4));
    }

    [Test]
    public void Combined_override_precedes_gestor_then_observed_cpf_then_default()
    {
        var config = new SyntheticBrazilianNameErrorConfig
        {
            Default = new() { DuplicateLetter = .1 },
            ByCpfStratum = new() { ["WITHOUT_CPF"] = new() { DuplicateParticle = .2 } },
            ByGestor = new() { ["G0"] = new() { PrefixTitle = .3 } },
            ByGestorAndCpfStratum = new()
            {
                ["G0/WITHOUT_CPF"] = new() { OmitAgnome = .4 }
            }
        };
        config.Validate(2);
        Assert.Multiple(() =>
        {
            Assert.That(config.Resolve("G0", false).OmitAgnome, Is.EqualTo(.4));
            Assert.That(config.Resolve("G0", false).PrefixTitle, Is.Zero);
            Assert.That(config.Resolve("G0", true).PrefixTitle, Is.EqualTo(.3));
            Assert.That(config.Resolve("G1", false).DuplicateParticle, Is.EqualTo(.2));
            Assert.That(config.Resolve("G1", true).DuplicateLetter, Is.EqualTo(.1));
        });
    }

    [Test]
    public void Configuration_hash_does_not_depend_on_dictionary_insertion_order()
    {
        var left = new SyntheticBrazilianNameErrorConfig
        {
            ByGestor = new() { ["G2"] = new() { DuplicateLetter = .2 }, ["G0"] = new() { DuplicateLetter = .1 } }
        };
        var right = new SyntheticBrazilianNameErrorConfig
        {
            ByGestor = new() { ["G0"] = new() { DuplicateLetter = .1 }, ["G2"] = new() { DuplicateLetter = .2 } }
        };
        Assert.That(left.ConfigSha256(), Is.EqualTo(right.ConfigSha256()));
        Assert.That(left.CanonicalJson(), Is.EqualTo(right.CanonicalJson()));
    }

    [Test]
    public void Brazilian_error_operators_are_effective_and_emit_separate_labels()
    {
        var doubles = Observe(
            "ANA SILVA", "MARIA LIMA",
            new() { Default = new() { DuplicateLetter = 1 } });
        Assert.Multiple(() =>
        {
            Assert.That(doubles.Name, Has.Length.EqualTo("ANA SILVA".Length + 1));
            Assert.That(doubles.MotherName, Has.Length.EqualTo("MARIA LIMA".Length + 1));
            Assert.That(doubles.Corruptions, Does.Contain("NOME_BR_DUPLICATE_LETTER"));
            Assert.That(doubles.Corruptions, Does.Contain("MAE_BR_DUPLICATE_LETTER"));
        });

        var particles = Observe(
            "ANA DE LIMA", "MARIA DAS SOUZA",
            new() { Default = new() { DuplicateParticle = 1 } });
        Assert.Multiple(() =>
        {
            Assert.That(particles.Name, Is.EqualTo("ANA DE DE LIMA"));
            Assert.That(particles.MotherName, Is.EqualTo("MARIA DAS DAS SOUZA"));
        });

        var apostrophe = Observe(
            "ANA SILVA", "MARIA LIMA",
            new() { Default = new() { SplitApostrophe = 1 } });
        Assert.That(apostrophe.Name, Does.Contain("'"));

        var title = Observe(
            "ANA SILVA", "MARIA LIMA",
            new() { Default = new() { PrefixTitle = 1 } });
        Assert.That(title.Name!.StartsWith("DR ", StringComparison.Ordinal)
            || title.Name.StartsWith("SGTO ", StringComparison.Ordinal), Is.True);

        var administrative = Observe(
            "ANA SILVA", "MARIA LIMA",
            new() { Default = new() { AdministrativeMarker = 1 } });
        Assert.Multiple(() =>
        {
            Assert.That(administrative.Name, Is.EqualTo("NAO INFORMADO"));
            Assert.That(administrative.MotherName, Is.EqualTo("NAO INFORMADO"));
            Assert.That(administrative.Corruptions, Does.Contain("NOME_BR_ADMINISTRATIVE_MARKER"));
        });
    }

    [Test]
    public void True_agnome_is_never_erased_from_truth_and_omission_is_only_an_observation_error()
    {
        var config = new SyntheticBrazilianNameErrorConfig
        {
            Default = new() { OmitAgnome = 1 }
        };
        var person = Person("JOAO SILVA FILHO");
        var observed = SyntheticCorpusGenerator.Observe(
            person, new Xoshiro256StarStar(402), NoLegacyErrors, "G0", 0, 1, 1, config);

        Assert.Multiple(() =>
        {
            Assert.That(person.Name, Is.EqualTo("JOAO SILVA FILHO"));
            Assert.That(observed.Name, Is.EqualTo("JOAO SILVA"));
            Assert.That(observed.Corruptions, Does.Contain("NOME_BR_OMIT_AGNOME"));
            Assert.That(BrazilianNameComponents.Project(person.Name)!.Agnome, Is.EqualTo("FILHO"));
            Assert.That(BrazilianNameComponents.Project(observed.Name)!.Agnome, Is.Null);
        });

        var father = Person("JOAO SILVA");
        Assert.That(BrazilianNameComponents.Project(father.Name)!.NormalizedFull,
            Is.Not.EqualTo(BrazilianNameComponents.Project(person.Name)!.NormalizedFull));
        var abbreviation = Observe(
            "JOAO SILVA JUNIOR", "MARIA SILVA",
            new() { Default = new() { AbbreviateAgnome = 1 } });
        Assert.That(abbreviation.Name, Is.EqualTo("JOAO SILVA JR"));
        Assert.That(abbreviation.Corruptions, Does.Contain("NOME_BR_ABBREVIATE_AGNOME"));
    }

    [Test]
    public void Observed_cpf_stratum_is_chosen_after_retention_and_changes_name_error()
    {
        var config = new SyntheticBrazilianNameErrorConfig
        {
            ByCpfStratum = new()
            {
                ["WITHOUT_CPF"] = new() { PrefixTitle = 1 },
                ["WITH_CPF"] = new() { AdministrativeMarker = 1 }
            }
        };
        var person = Person("ANA SILVA", cpf: "12345678909");
        var dropped = SyntheticCorpusGenerator.Observe(
            person, new Xoshiro256StarStar(21), NoLegacyErrors, "G0", 0, 0, 1, config);
        var retained = SyntheticCorpusGenerator.Observe(
            person, new Xoshiro256StarStar(21), NoLegacyErrors, "G0", 1, 1, 1, config);
        Assert.Multiple(() =>
        {
            Assert.That(dropped.Cpf, Is.Null);
            Assert.That(dropped.Name!.StartsWith("DR ", StringComparison.Ordinal)
                || dropped.Name.StartsWith("SGTO ", StringComparison.Ordinal), Is.True);
            Assert.That(retained.Cpf, Is.EqualTo(person.Cpf));
            Assert.That(retained.Name, Is.EqualTo("NAO INFORMADO"));
            Assert.That(retained.Corruptions, Does.Contain("NOME_BR_ADMINISTRATIVE_MARKER"));
        });
    }

    [Test]
    public void Generator_is_deterministic_with_opt_in_and_legacy_path_is_unmodified()
    {
        var config = new SyntheticBrazilianNameErrorConfig
        {
            AgnomeBasePrevalence = 1,
            Default = new() { AbbreviateAgnome = 1 }
        };
        var legacy = new SyntheticCorpusOptions(People: 75, Seed: 250, ErrorProfile: "clean");
        var experimental = legacy with { BrazilianNameErrors = config };
        var a = Generator().Generate(experimental);
        var b = Generator().Generate(experimental);
        var c = Generator().Generate(legacy);
        var d = Generator().Generate(legacy with { BrazilianNameErrors = null });
        Assert.Multiple(() =>
        {
            Assert.That(a.People.Select(x => (x.Name, x.Cpf, x.MotherName)),
                Is.EqualTo(b.People.Select(x => (x.Name, x.Cpf, x.MotherName))));
            Assert.That(a.Observations.Select(x => (x.Name, x.Corruptions)),
                Is.EqualTo(b.Observations.Select(x => (x.Name, x.Corruptions))));
            Assert.That(a.People.All(x => BrazilianNameComponents.Project(x.Name)!.Agnome is not null), Is.True);
            Assert.That(c.People.Select(x => (x.Name, x.Cpf)),
                Is.EqualTo(d.People.Select(x => (x.Name, x.Cpf))));
            Assert.That(c.Observations.Select(x => (x.Name, x.Corruptions)),
                Is.EqualTo(d.Observations.Select(x => (x.Name, x.Corruptions))));
        });
    }

    [Test]
    public async Task Experimental_manifest_and_truth_include_config_hash_and_realized_counts()
    {
        var config = new SyntheticBrazilianNameErrorConfig
        {
            Default = new() { DuplicateLetter = 1 }
        };
        var opts = new SyntheticCorpusOptions(People: 12, Seed: 11, BrazilianNameErrors: config);
        var gen = Generator().Generate(opts);
        var source = new SyntheticCorpusNominalSource(
            new SyntheticFrequencySampler(
                new[] { new SyntheticFrequencyValue("ANA", 100), new SyntheticFrequencyValue("JOAO", 90) }, 1),
            new SyntheticFrequencySampler(
                new[] { new SyntheticFrequencyValue("SILVA", 100), new SyntheticFrequencyValue("LIMA", 80) }, 1),
            "FIXTURE", "fixture.ndjson.gz", new string('A', 64), new string('B', 64), 2, 2, 2);
        var root = Path.Combine(Path.GetTempPath(), "jornada-br-synth-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fingerprint = SyntheticCorpusInputIdentity.ComputeFingerprint(opts.Seed, Array.Empty<IbgeProjectionFile>(), config);
            var legacyFingerprint = SyntheticCorpusInputIdentity.ComputeFingerprint(opts.Seed, Array.Empty<IbgeProjectionFile>());
            Assert.That(fingerprint, Is.Not.EqualTo(legacyFingerprint));
            var paths = await SyntheticCorpusMaterializer.WriteAsync(root, gen, source, fingerprint);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(paths.ManifestPath));
            using var truth = JsonDocument.Parse(await File.ReadAllTextAsync(paths.TruthPath));
            Assert.Multiple(() =>
            {
                Assert.That(manifest.RootElement.GetProperty("brazilian_name_errors")
                    .GetProperty("config_sha256").GetString(), Is.EqualTo(config.ConfigSha256()));
                Assert.That(truth.RootElement.GetProperty("brazilian_name_errors_config_sha256").GetString(),
                    Is.EqualTo(config.ConfigSha256()));
                Assert.That(truth.RootElement.GetProperty("brazilian_name_errors_realized_label_counts")
                    .EnumerateObject().Any(), Is.True);
                Assert.That(truth.RootElement.GetProperty("brazilian_name_errors_rates_source").GetString(),
                    Is.EqualTo("synthetic_configured_rates_not_empirical"));
            });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static SyntheticCorpusGenerator Generator()
        => new(
            new SyntheticFrequencySampler(
                new[] { new SyntheticFrequencyValue("ANA", 100), new SyntheticFrequencyValue("JOAO", 90) }, 1),
            new SyntheticFrequencySampler(
                new[] { new SyntheticFrequencyValue("SILVA", 100), new SyntheticFrequencyValue("LIMA", 80) }, 1));

    private static SyntheticPerson Person(string name, string? cpf = null)
        => new()
        {
            BasePersonId = "P0000001",
            Partition = "TEST",
            Name = name,
            MotherName = "MARIA DE LIMA",
            BirthDate = new DateOnly(1980, 7, 27),
            Sex = "M",
            Cpf = cpf,
            EvaluationWeight = 1
        };

    private static SyntheticObservation Observe(
        string name, string mother, SyntheticBrazilianNameErrorConfig config)
        => SyntheticCorpusGenerator.Observe(
            new SyntheticPerson
            {
                BasePersonId = "P0000001",
                Partition = "TEST",
                Name = name,
                MotherName = mother,
                BirthDate = new DateOnly(1980, 7, 27),
                Sex = "M",
                EvaluationWeight = 1
            },
            new Xoshiro256StarStar(10), NoLegacyErrors, "G0", 0, 1, 1, config);
}
