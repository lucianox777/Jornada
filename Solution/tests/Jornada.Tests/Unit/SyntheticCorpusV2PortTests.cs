using System.Text.Json;
using Jornada.Linkage.SyntheticCorpus;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SyntheticCorpusV2PortTests
{
    [Test]
    public void Csharp_profiles_match_python_v2_declared_rates()
    {
        Assert.Multiple(() =>
        {
            AssertProfile("clean", .06, .10, .04, .00, .05, .01);
            AssertProfile("independent", .22, .30, .15, .00, .18, .05);
            AssertProfile("correlated", .12, .16, .08, .18, .18, .05);
            AssertProfile("field", .35, .45, .28, .22, .40, .12);
        });
    }

    [Test]
    public void Cpf_has_valid_check_digits_and_rejects_uniform_seed_body()
    {
        var random = new Xoshiro256StarStar(42);
        for (var n = 0; n < 1000; n++)
        {
            var cpf = SyntheticCorpusV2Rules.GenerateCpf(random);
            Assert.That(cpf, Has.Length.EqualTo(11));
            Assert.That(cpf.Take(9).Distinct().Count(), Is.GreaterThan(1));

            var digits = cpf.Select(c => c - '0').ToArray();
            var sum1 = Enumerable.Range(0, 9).Sum(i => digits[i] * (10 - i));
            var raw1 = 11 - (sum1 % 11);
            var dv1 = raw1 >= 10 ? 0 : raw1;
            var sum2 = Enumerable.Range(0, 9).Sum(i => digits[i] * (11 - i)) + dv1 * 2;
            var raw2 = 11 - (sum2 % 11);
            var dv2 = raw2 >= 10 ? 0 : raw2;
            Assert.That(digits[^2..], Is.EqualTo(new[] { dv1, dv2 }));
        }
    }

    [Test]
    public void Cns_provisional_rule_matches_python_v2_and_invalidation_breaks_it()
    {
        var random = new Xoshiro256StarStar(43);
        for (var n = 0; n < 1000; n++)
        {
            var cns = SyntheticCorpusV2Rules.GenerateCns(random);
            Assert.That(cns, Has.Length.EqualTo(15));
            Assert.That(cns[0], Is.AnyOf('7', '8', '9'));

            var weighted = cns.Select((c, i) => (c - '0') * (15 - i)).Sum();
            Assert.That(weighted % 11, Is.Zero);

            var invalid = SyntheticCorpusV2Rules.InvalidateCheckDigit(cns)!;
            var invalidWeighted = invalid.Select((c, i) => (c - '0') * (15 - i)).Sum();
            Assert.That(invalidWeighted % 11, Is.Not.Zero);
        }
    }

    [Test]
    public void Date_corruption_is_always_effective_including_day_greater_than_twelve()
    {
        var random = new Xoshiro256StarStar(44);
        var source = new DateOnly(1984, 7, 27);

        for (var n = 0; n < 1000; n++)
        {
            var corrupted = SyntheticCorpusV2Rules.CorruptDate(source, random);
            Assert.Multiple(() =>
            {
                Assert.That(corrupted.Operation, Is.Not.Null);
                Assert.That(corrupted.Value, Is.Not.EqualTo(source));
            });
        }
    }

    [TestCase(4)]
    [TestCase(1936)]
    [TestCase(2000)]
    [TestCase(2020)]
    [TestCase(2096)]
    [TestCase(9996)]
    public void February_29_year_corruption_uses_effective_leap_year_fallback(int year)
    {
        var source = new DateOnly(year, 2, 29);
        var random = new Xoshiro256StarStar(44);
        var yearCorruptionObserved = false;

        for (var n = 0; n < 1024; n++)
        {
            var corrupted = SyntheticCorpusV2Rules.CorruptDate(source, random);
            Assert.That(corrupted.Value, Is.Not.EqualTo(source));
            Assert.That(corrupted.Operation, Is.Not.Null);
            if (corrupted.Operation != "DATE_YEAR")
                continue;

            yearCorruptionObserved = true;
            Assert.Multiple(() =>
            {
                Assert.That(corrupted.Value.Year, Is.AnyOf(year - 4, year + 4));
                Assert.That(corrupted.Value.Month, Is.EqualTo(2));
                Assert.That(corrupted.Value.Day, Is.EqualTo(29));
            });
        }

        Assert.That(yearCorruptionObserved, Is.True,
            "A regressão deve exercitar explicitamente a operação DATE_YEAR.");
    }

    [Test]
    public void Empirical_m_matches_python_v2_missing_denominator_rule()
    {
        var rows = new[]
        {
            Observation("O1", "P1", "A", "M", new DateOnly(2000,1,1)),
            Observation("O2", "P1", "A", null, new DateOnly(2000,1,1)),
            Observation("O3", "P1", "B", "M", null)
        };

        var m = SyntheticCorpusGenerator.ComputeEmpiricalM(rows);

        Assert.Multiple(() =>
        {
            Assert.That(m["NOME"].EligiblePairs, Is.EqualTo(3));
            Assert.That(m["NOME"].ExactPairs, Is.EqualTo(1));
            Assert.That(m["NOME"].MExactEmpirical, Is.EqualTo(.333333));
            Assert.That(m["NOME_MAE"].EligiblePairs, Is.EqualTo(1));
            Assert.That(m["NOME_MAE"].MExactEmpirical, Is.EqualTo(1.0));
            Assert.That(m["NASCIMENTO"].EligiblePairs, Is.EqualTo(1));
            Assert.That(m["NASCIMENTO"].MExactEmpirical, Is.EqualTo(1.0));
        });
    }

    [Test]
    public void Cns_scenarios_never_change_truth_identity_and_create_anomalies()
    {
        var random = new Xoshiro256StarStar(45);
        var people = Enumerable.Range(0, 60)
            .Select(i => new SyntheticPerson
            {
                BasePersonId = $"P{i}",
                Partition = "TRAIN",
                Name = $"Pessoa {i}",
                MotherName = $"Mae {i}",
                BirthDate = new DateOnly(1940 + i, 1, 1),
                Sex = i % 2 == 0 ? "M" : "F",
                Cpf = null,
                Cns = SyntheticCorpusV2Rules.GenerateCns(random),
                EvaluationWeight = 1
            })
            .ToArray();
        var before = people.Select(x => x.BasePersonId).ToArray();

        SyntheticCorpusGenerator.ApplyCnsScenarios(people, random, .10, .10, .10);

        Assert.Multiple(() =>
        {
            Assert.That(people.Select(x => x.BasePersonId), Is.EqualTo(before));
            Assert.That(people.Any(x => x.CnsScenario != "CLEAN"), Is.True);
            Assert.That(people.All(x => x.CnsScenario is not null), Is.True);
        });
    }

    [Test]
    public void Generator_is_deterministic_for_same_seed_and_rules()
    {
        var first = new SyntheticFrequencySampler(new[]
        {
            new SyntheticFrequencyValue("ANA", 100),
            new SyntheticFrequencyValue("JOAO", 90),
            new SyntheticFrequencyValue("MARIA", 80),
            new SyntheticFrequencyValue("CARLOS", 70)
        }, 1);
        var surname = new SyntheticFrequencySampler(new[]
        {
            new SyntheticFrequencyValue("SILVA", 100),
            new SyntheticFrequencyValue("SOUZA", 80),
            new SyntheticFrequencyValue("LIMA", 60),
            new SyntheticFrequencyValue("COSTA", 40)
        }, 1);
        var options = new SyntheticCorpusOptions(People: 1000, Seed: 355, ErrorProfile: "correlated");

        var left = new SyntheticCorpusGenerator(first, surname).Generate(options);
        var right = new SyntheticCorpusGenerator(first, surname).Generate(options);

        Assert.Multiple(() =>
        {
            Assert.That(
                left.People.Select(x => (x.BasePersonId, x.Partition, x.Name, x.MotherName, x.BirthDate, x.Cpf, x.Cns, x.CnsScenario)),
                Is.EqualTo(right.People.Select(x => (x.BasePersonId, x.Partition, x.Name, x.MotherName, x.BirthDate, x.Cpf, x.Cns, x.CnsScenario))));
            Assert.That(
                left.Observations.Select(x => (x.ObservationId, x.Name, x.MotherName, x.BirthDate, x.Cpf, x.Cns, x.Corruptions)),
                Is.EqualTo(right.Observations.Select(x => (x.ObservationId, x.Name, x.MotherName, x.BirthDate, x.Cpf, x.Cns, x.Corruptions))));
            Assert.That(left.EmpiricalMExact, Is.EqualTo(right.EmpiricalMExact));
        });
    }

    [Test]
    public async Task Materialization_is_byte_stable_and_truth_json_is_self_describing()
    {
        var first = new SyntheticFrequencySampler(new[]
        {
            new SyntheticFrequencyValue("ANA", 100),
            new SyntheticFrequencyValue("JOAO", 90)
        }, 1);
        var surname = new SyntheticFrequencySampler(new[]
        {
            new SyntheticFrequencyValue("SILVA", 100),
            new SyntheticFrequencyValue("LIMA", 80)
        }, 1);
        var options = new SyntheticCorpusOptions(People: 50, Seed: 99, ErrorProfile: "clean");
        var generation = new SyntheticCorpusGenerator(first, surname).Generate(options);
        var source = new SyntheticCorpusNominalSource(
            first, surname, "FIXTURE", "projection/frequencia-brasil.ndjson.gz",
            new string('A', 64), new string('B', 64), 4, 2, 2);

        var root = Path.Combine(Path.GetTempPath(), "jornada-corpus-port-" + Guid.NewGuid().ToString("N"));
        var a = Path.Combine(root, "a");
        var b = Path.Combine(root, "b");
        try
        {
            var left = await SyntheticCorpusMaterializer.WriteAsync(a, generation, source, new string('C', 64));
            var right = await SyntheticCorpusMaterializer.WriteAsync(b, generation, source, new string('C', 64));

            foreach (var file in new[] { "pessoas_verdade.csv", "observacoes.csv", "gabarito.json", "generation-manifest.json" })
            {
                Assert.That(
                    await File.ReadAllBytesAsync(Path.Combine(a, file)),
                    Is.EqualTo(await File.ReadAllBytesAsync(Path.Combine(b, file))),
                    file);
            }

            using var truth = JsonDocument.Parse(await File.ReadAllTextAsync(left.TruthPath));
            Assert.Multiple(() =>
            {
                Assert.That(truth.RootElement.GetProperty("generator_version").GetString(),
                    Is.EqualTo(SyntheticCorpusInputIdentity.GeneratorVersion));
                Assert.That(truth.RootElement.GetProperty("ruleset_version").GetString(),
                    Is.EqualTo(SyntheticCorpusV2Rules.RulesetVersion));
                Assert.That(truth.RootElement.GetProperty("empirical_m_exact").TryGetProperty("NOME", out _), Is.True);
            });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void AssertProfile(
        string name,
        double pName,
        double pMother,
        double pDate,
        double pCommon,
        double pMissingMother,
        double pMissingDate)
    {
        var profile = SyntheticCorpusV2Rules.Profiles[name];
        Assert.That(
            new[]
            {
                profile.NameCorruptionProbability,
                profile.MotherCorruptionProbability,
                profile.DateCorruptionProbability,
                profile.CommonCorruptionProbability,
                profile.MissingMotherProbability,
                profile.MissingDateProbability
            },
            Is.EqualTo(new[] { pName, pMother, pDate, pCommon, pMissingMother, pMissingDate }),
            name);
    }

    private static SyntheticObservation Observation(
        string id,
        string person,
        string? name,
        string? mother,
        DateOnly? birth)
        => new()
        {
            ObservationId = id,
            BasePersonId = person,
            Partition = "TEST",
            Gestor = "G0",
            Name = name,
            MotherName = mother,
            BirthDate = birth,
            Sex = "F",
            Cpf = null,
            Cns = null,
            EvaluationWeight = 1,
            Corruptions = string.Empty
        };
}
