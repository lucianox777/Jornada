using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Linkage.SyntheticCorpus;

internal static class FamilySamplingSmoke
{
    internal static async Task RunAsync()
    {
        // Artificial tables and tiny diagnostic fixture only; not IBGE data
        // and not an approved family population, calibration, or physical test.
        var root = Path.Combine(Path.GetTempPath(), "family-sampling-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "births.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                schema_version = SyntheticDailyBirthDistribution.Schema,
                source = "ARTIFICIAL_SMOKE", reference_period = "SMOKE", geography = "SMOKE",
                rows = Enumerable.Range(1900, 126).Select(year => new
                { date = $"{year:D4}-01-01", births = 1 }).ToArray()
            }));
            var births = await SyntheticDailyBirthDistribution.LoadAsync(path);
            SyntheticFrequencySampler Sampler(params string[] values) =>
                new(values.Select(v => new SyntheticFrequencyValue(v, 10)), 1);
            var first = Sampler("ANA", "ANNA", "ALFA", "OMEGA", "BETA", "GAMA");
            var surname = Sampler("SILVA", "SOUZA");
            var mothers = Sampler("ANA", "ANNA", "MARIA");
            var sha = new string('A', 64);
            var nominal = new SyntheticCorpusDemographicNominalSource(
                first, surname, mothers, surname, "ARTIFICIAL_SMOKE",
                "person", sha, sha, 8, "mother-first", sha, sha, 3,
                "mother-surname", sha, sha, 2, 6, 2, 3, 2);
            var quotas = Enum.GetValues<SyntheticFamilyCaseKind>().Select(k =>
                new SyntheticFamilyCaseQuota(k, 1, k == SyntheticFamilyCaseKind.LargerSiblingGroup ? 4 : 2)).ToArray();
            var request = new SyntheticFamilyPlanRequest(20, 4, 8, 32, quotas);
            var rules = new SyntheticFamilyAttributeRules(new(1900, 1, 1), new(2025, 1, 1),
                300, 15 * 365, 55 * 366, 1);
            var options = new SyntheticFamilySamplingOptions(789, rules, 25 * 366, 20000, .5, 60, 20, 20);
            var actual = SyntheticFamilyConditionalSampler.Sample(request, options, nominal, births, 1);
            var repeat = SyntheticFamilyConditionalSampler.Sample(
                request with { Quotas = quotas.Reverse().ToArray() }, options, nominal, births, 1);
            if (actual.Fixture.People.Count != 20 ||
                !actual.Fixture.People.SequenceEqual(repeat.Fixture.People) ||
                actual.Fixture.PrivateTruth != repeat.Fixture.PrivateTruth ||
                actual.Receipt != repeat.Receipt ||
                actual.Receipt.ProvenanceSha256 != Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(actual.Receipt.ProvenanceJson))))
                throw new InvalidOperationException("Conditional family sampling not deterministic");
            using var receipt = JsonDocument.Parse(actual.Receipt.ProvenanceJson);
            if (receipt.RootElement.GetProperty("jointWeightsAvailable").GetBoolean() ||
                receipt.RootElement.GetProperty("birthSha256").GetString() != births.Provenance.Sha256 ||
                actual.Receipt.TruthSha256 != actual.Fixture.PrivateTruth.TruthSha256 ||
                actual.Receipt.AttributesSha256 != actual.Fixture.PrivateTruth.AttributesSha256)
                throw new InvalidOperationException("Conditional family provenance mismatch");

            var trainOnly = SyntheticFamilyConditionalSampler.Sample(request,
                options with { TrainPercent = 100, ValidationPercent = 0, TestPercent = 0 }, nominal, births, 1);
            if (trainOnly.Fixture.People.Any(p => p.Partition != "TRAIN"))
                throw new InvalidOperationException("Conditional family partition mismatch");

            var impossible = new SyntheticFamilyPlanRequest(2, 4, 8, 32,
                [new(SyntheticFamilyCaseKind.SimilarNameTwins, 1, 2)]);
            try
            {
                SyntheticFamilyConditionalSampler.Sample(impossible, options with { MaximumAttemptsPerConstraint = 8 },
                    nominal with { PersonFirstNames = Sampler("ANA") }, births, 1);
                throw new InvalidOperationException("Unsupported nominal constraint accepted");
            }
            catch (InvalidDataException ex) when (ex.Message == "FAMILY_SAMPLING_SUPPORT_EXHAUSTED_TWIN_FIRST_NAME") { }
            try
            {
                SyntheticFamilyConditionalSampler.Sample(impossible,
                    options with { AttributeRules = rules with { EarliestBirth = new(2025, 1, 2), LatestBirth = new(2025, 1, 2) },
                        MaximumAttemptsPerConstraint = 200 },
                    nominal with { PersonFirstNames = Sampler("ANA", "ANNA") }, births, 1);
                throw new InvalidOperationException("Unsupported date constraint accepted");
            }
            catch (InvalidDataException ex) when (ex.Message == "FAMILY_SAMPLING_SUPPORT_EXHAUSTED_BIRTH_RANGE") { }
            Console.WriteLine("FAMILY_CONDITIONAL_SAMPLING_SMOKE_PASS");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
