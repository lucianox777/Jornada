using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Linkage.SyntheticCorpus;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SyntheticDemographicPrimaryTests
{
    [Test]
    public async Task Generator_uses_separate_person_mother_and_daily_birth_sources()
    {
        var dir = CreateTempDirectory();
        try
        {
            var birthPath = Path.Combine(dir, "births.json");
            await File.WriteAllTextAsync(birthPath, """
            {
              "schema_version":"JORNADA_SYNTH_BIRTH_DAILY_V1",
              "source":"FIXTURE_DAILY_BIRTHS",
              "reference_period":"1988",
              "geography":"SAO_PAULO_SP",
              "rows":[{"date":"1988-07-07","births":100}]
            }
            """);

            var birth = await SyntheticDailyBirthDistribution.LoadAsync(birthPath);
            var personFirst = Sampler("PAULO");
            var personSurname = Sampler("SILVA");
            var motherFirst = Sampler("MARIA");
            var motherSurname = Sampler("SOUZA");
            var generator = new SyntheticCorpusGenerator(
                personFirst, personSurname, motherFirst, motherSurname, birth);
            var generation = generator.Generate(new SyntheticCorpusOptions(
                People: 40,
                Seed: 3550308,
                ErrorProfile: "clean",
                CpfBasePrevalence: 0,
                CnsBasePrevalence: 0));

            Assert.Multiple((Action)(() =>
            {
                Assert.That(generation.People, Has.Count.EqualTo(40));
                Assert.That(generation.People.All(x => x.Name.StartsWith("PAULO ", StringComparison.Ordinal)), Is.True);
                Assert.That(generation.People.All(x => x.Name.Contains("SILVA", StringComparison.Ordinal)), Is.True);
                Assert.That(generation.People.All(x => x.MotherName.StartsWith("MARIA ", StringComparison.Ordinal)), Is.True);
                Assert.That(generation.People.All(x => x.MotherName.EndsWith("SOUZA", StringComparison.Ordinal)), Is.True);
                Assert.That(generation.People.All(x => x.BirthDate == new DateOnly(1988, 7, 7)), Is.True);
                Assert.That(birth.Provenance.SchemaVersion, Is.EqualTo(SyntheticDailyBirthDistribution.Schema));
                Assert.That(birth.Provenance.Sha256, Does.Match("^[0-9A-F]{64}$"));
            }));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task Loader_selects_SP_person_Brazil_female_mother_and_Brazil_surname()
    {
        var root = CreateTempDirectory();
        try
        {
            var files = new[]
            {
                await WriteProjectionAsync(root, "projection/municipio.ndjson.gz", "MUNICIPIO",
                    """
                    {"tipo":"NOME","valor":"PAULO","frequencia":100,"escopoGeografico":"MUNICIPIO","ufCodigo":"35","municipioCodigo":"3550308"}
                    {"tipo":"SOBRENOME","valor":"SILVA","frequencia":90,"escopoGeografico":"MUNICIPIO","ufCodigo":"35","municipioCodigo":"3550308"}
                    {"tipo":"NOME","valor":"CARLOS","frequencia":999,"escopoGeografico":"MUNICIPIO","ufCodigo":"33","municipioCodigo":"3304557"}
                    """),
                await WriteProjectionAsync(root, "projection/sexo.ndjson.gz", "BRASIL_SEXO",
                    """
                    {"tipo":"NOME","valor":"MARIA","frequencia":200,"sexo":"FEMININO","escopoGeografico":"BRASIL"}
                    {"tipo":"NOME","valor":"JOAO","frequencia":300,"sexo":"MASCULINO","escopoGeografico":"BRASIL"}
                    """),
                await WriteProjectionAsync(root, "projection/brasil.ndjson.gz", "BRASIL_TOTAL",
                    """
                    {"tipo":"SOBRENOME","valor":"SOUZA","frequencia":250,"escopoGeografico":"BRASIL"}
                    {"tipo":"NOME","valor":"ANA","frequencia":180,"escopoGeografico":"BRASIL"}
                    """)
            };
            var manifest = new
            {
                schemaVersion = 1,
                referenceCode = "CENSO2022_NOMES_BRASIL_V1",
                format = "NDJSON_UTF8_GZIP",
                generatedFrom = "fixture",
                files
            };
            await File.WriteAllTextAsync(
                Path.Combine(root, "projection-manifest.json"),
                JsonSerializer.Serialize(manifest));

            var source = await SyntheticCorpusSourceLoader.LoadDemographicPrimaryAsync(
                root,
                new SyntheticCorpusOptions(People: 1, MinFrequency: 1));

            var random = new Xoshiro256StarStar(42);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(source.PersonFirstNames.Draw(random), Is.EqualTo("PAULO"));
                Assert.That(source.PersonSurnames.Draw(random), Is.EqualTo("SILVA"));
                Assert.That(source.MotherFirstNames.Draw(random), Is.EqualTo("MARIA"));
                Assert.That(source.MotherSurnames.Draw(random), Is.EqualTo("SOUZA"));
                Assert.That(source.PersonFirstNameCount, Is.EqualTo(1));
                Assert.That(source.PersonSurnameCount, Is.EqualTo(1));
                Assert.That(source.MotherFirstNameCount, Is.EqualTo(1));
                Assert.That(source.MotherSurnameCount, Is.EqualTo(1));
            }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Daily_birth_distribution_fails_closed_on_missing_or_invalid_frequency()
    {
        Assert.ThrowsAsync<FileNotFoundException>((Func<Task>)(async () =>
            await SyntheticDailyBirthDistribution.LoadAsync(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"))));

        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "invalid.json");
            File.WriteAllText(path, """
            {
              "schema_version":"JORNADA_SYNTH_BIRTH_DAILY_V1",
              "source":"FIXTURE",
              "reference_period":"1988",
              "geography":"SAO_PAULO_SP",
              "rows":[{"date":"1988-01-01","births":0}]
            }
            """);
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                await SyntheticDailyBirthDistribution.LoadAsync(path)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static SyntheticFrequencySampler Sampler(string value)
        => new([new SyntheticFrequencyValue(value, 100)], 1);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "jornada-synthetic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<object> WriteProjectionAsync(
        string root,
        string relativePath,
        string kind,
        string ndjson)
    {
        var canonical = ndjson.Trim().Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal) + "\n";
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using (var output = File.Create(full))
        await using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize))
            await gzip.WriteAsync(bytes);
        var physical = await File.ReadAllBytesAsync(full);
        return new
        {
            path = relativePath,
            kind,
            required = true,
            sha256 = Convert.ToHexString(SHA256.HashData(physical)),
            canonicalContentSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            rowCount = canonical.Count(c => c == '\n')
        };
    }
}
