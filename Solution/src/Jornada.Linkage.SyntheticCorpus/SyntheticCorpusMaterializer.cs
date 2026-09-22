using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jornada.Linkage.SyntheticCorpus;

public sealed record SyntheticCorpusMaterializationResult(
    string PeoplePath,
    string ObservationsPath,
    string TruthPath,
    string ManifestPath,
    IReadOnlyDictionary<string, string> OutputSha256);

public static class SyntheticCorpusMaterializer
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    public static async Task<SyntheticCorpusMaterializationResult> WriteAsync(
        string outputDirectory,
        SyntheticCorpusGeneration generation,
        SyntheticCorpusNominalSource source,
        string inputFingerprintSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFingerprintSha256);

        Directory.CreateDirectory(outputDirectory);
        var peoplePath = Path.Combine(outputDirectory, "pessoas_verdade.csv");
        var observationsPath = Path.Combine(outputDirectory, "observacoes.csv");
        var truthPath = Path.Combine(outputDirectory, "gabarito.json");
        var manifestPath = Path.Combine(outputDirectory, "generation-manifest.json");

        await File.WriteAllTextAsync(
            peoplePath,
            BuildPeopleCsv(generation.People),
            Utf8NoBom,
            cancellationToken);
        await File.WriteAllTextAsync(
            observationsPath,
            BuildObservationsCsv(generation.Observations),
            Utf8NoBom,
            cancellationToken);
        await File.WriteAllTextAsync(
            truthPath,
            BuildTruthJson(generation, source, inputFingerprintSha256),
            Utf8NoBom,
            cancellationToken);

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pessoas_verdade.csv"] = await HashFileAsync(peoplePath, cancellationToken),
            ["observacoes.csv"] = await HashFileAsync(observationsPath, cancellationToken),
            ["gabarito.json"] = await HashFileAsync(truthPath, cancellationToken)
        };

        var manifest = new
        {
            schema_version = 1,
            generator_version = SyntheticCorpusInputIdentity.GeneratorVersion,
            ruleset_version = SyntheticCorpusV2Rules.RulesetVersion,
            rng_version = Xoshiro256StarStar.AlgorithmVersion,
            frequency_sampler_version = SyntheticFrequencySampler.MethodVersion,
            date_corruption_version = SyntheticCorpusV2Rules.DateCorruptionVersion,
            seed = generation.Options.Seed,
            input_fingerprint_sha256 = inputFingerprintSha256.ToUpperInvariant(),
            reference_code = source.ReferenceCode,
            outputs = hashes
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new { path = x.Key, sha256 = x.Value })
                .ToArray()
        };
        var manifestJson = JsonSerializer.Serialize(
            manifest,
            IndentedJsonOptions) + "\n";
        if (generation.Options.BrazilianNameErrors is { } experimental)
        {
            var annotated = JsonNode.Parse(manifestJson)!.AsObject();
            annotated["brazilian_name_errors"] = JsonSerializer.SerializeToNode(new
            {
                version = experimental.Version,
                config_sha256 = experimental.ConfigSha256(),
                source = "synthetic_configured_rates_not_empirical"
            });
            manifestJson = JsonSerializer.Serialize(annotated, IndentedJsonOptions) + "\n";
        }
        await File.WriteAllTextAsync(manifestPath, manifestJson, Utf8NoBom, cancellationToken);

        return new SyntheticCorpusMaterializationResult(
            peoplePath,
            observationsPath,
            truthPath,
            manifestPath,
            hashes);
    }

    private static string BuildPeopleCsv(IReadOnlyList<SyntheticPerson> people)
    {
        var builder = new StringBuilder();
        AppendCsvRow(builder,
            "base_person_id", "particao", "nome", "nome_mae", "data_nascimento",
            "sexo", "cpf", "cns", "cns_scenario", "evaluation_weight");
        foreach (var person in people)
        {
            AppendCsvRow(
                builder,
                person.BasePersonId,
                person.Partition,
                person.Name,
                person.MotherName,
                person.BirthDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                person.Sex,
                person.Cpf,
                person.Cns,
                person.CnsScenario,
                FormatWeight(person.EvaluationWeight));
        }

        return builder.ToString();
    }

    private static string BuildObservationsCsv(IReadOnlyList<SyntheticObservation> observations)
    {
        var builder = new StringBuilder();
        AppendCsvRow(builder,
            "observacao_id", "base_person_id", "particao", "gestor", "nome", "nome_mae",
            "data_nascimento", "sexo", "cpf", "cns", "corrupcoes", "evaluation_weight");
        foreach (var observation in observations)
        {
            AppendCsvRow(
                builder,
                observation.ObservationId,
                observation.BasePersonId,
                observation.Partition,
                observation.Gestor,
                observation.Name,
                observation.MotherName,
                observation.BirthDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                observation.Sex,
                observation.Cpf,
                observation.Cns,
                observation.Corruptions,
                FormatWeight(observation.EvaluationWeight));
        }

        return builder.ToString();
    }

    private static string BuildTruthJson(
        SyntheticCorpusGeneration generation,
        SyntheticCorpusNominalSource source,
        string inputFingerprintSha256)
    {
        var profile = SyntheticCorpusV2Rules.Profiles[generation.Options.ErrorProfile];
        var empirical = generation.EmpiricalMExact
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => new
                {
                    eligible_pairs = x.Value.EligiblePairs,
                    exact_pairs = x.Value.ExactPairs,
                    m_exact_empirical = x.Value.MExactEmpirical,
                    m_exact_empirical_reweighted = x.Value.MExactEmpiricalReweighted
                },
                StringComparer.Ordinal);

        var truth = new
        {
            generator_version = SyntheticCorpusInputIdentity.GeneratorVersion,
            ruleset_version = SyntheticCorpusV2Rules.RulesetVersion,
            rng_version = Xoshiro256StarStar.AlgorithmVersion,
            seed = generation.Options.Seed,
            people = generation.People.Count,
            observations = generation.Observations.Count,
            error_profile = generation.Options.ErrorProfile,
            input_fingerprint_sha256 = inputFingerprintSha256.ToUpperInvariant(),
            ibge = new
            {
                reference_code = source.ReferenceCode,
                source_path = source.SourcePath,
                // Compatibilidade semântica com gen_corpus_v2.py: esses dois campos
                // historicamente hashavam os bytes NDJSON descomprimidos, não o gzip físico.
                source_sha256_name_pass = source.CanonicalContentSha256,
                source_sha256_surname_pass = source.CanonicalContentSha256,
                source_physical_sha256 = source.PhysicalSha256,
                source_canonical_sha256 = source.CanonicalContentSha256,
                min_freq = generation.Options.MinFrequency,
                tail_oversample = generation.Options.TailOversample,
                first_name_rows = source.FirstNameCount,
                surname_rows = source.SurnameCount
            },
            identifier_model = new
            {
                cpf_base_prevalence = generation.Options.CpfBasePrevalence,
                cns_base_prevalence = generation.Options.CnsBasePrevalence,
                cpf_observation_retention = generation.Options.CpfObservationRetention,
                cns_observation_retention = generation.Options.CnsObservationRetention,
                cns_invalid_rate = generation.Options.CnsInvalidRate,
                cns_reuse_rate = generation.Options.CnsReuseRate,
                cns_dob_conflict_rate = generation.Options.CnsDobConflictRate
            },
            declared_corruption_rates = new
            {
                p_name = profile.NameCorruptionProbability,
                p_mother = profile.MotherCorruptionProbability,
                p_date = profile.DateCorruptionProbability,
                p_common = profile.CommonCorruptionProbability,
                p_missing_mother = profile.MissingMotherProbability,
                p_missing_date = profile.MissingDateProbability
            },
            empirical_m_exact = empirical,
            invariants = new[]
            {
                "base_person_id is truth-only; forbidden in blocking/scoring",
                "missing pairs excluded from field m denominator",
                "CPF/CNS label source and derivatives forbidden from candidate generation and scoring",
                "CNS scenarios are benchmark evidence only and never authorize UUID resolution",
                "date corruption attempts are effective; invalid day/month transpose never silently becomes no-op"
            }
        };

        var truthJson = JsonSerializer.Serialize(
            truth,
            IndentedJsonOptions) + "\n";
        if (generation.Options.BrazilianNameErrors is { } experimental)
        {
            // Somente no gabarito, inacessível ao calibrador antes do RASCUNHO.
            var annotated = JsonNode.Parse(truthJson)!.AsObject();
            annotated["brazilian_name_errors"] = JsonNode.Parse(experimental.CanonicalJson());
            annotated["brazilian_name_errors_config_sha256"] = experimental.ConfigSha256();
            annotated["brazilian_name_errors_rates_source"] = "synthetic_configured_rates_not_empirical";
            annotated["brazilian_name_errors_cpf_stratum"] = "observed_cpf_after_retention";
            var realized = generation.Observations
                .SelectMany(observation => observation.Corruptions
                    .Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Where(label => label.StartsWith("NOME_BR_", StringComparison.Ordinal)
                        || label.StartsWith("MAE_BR_", StringComparison.Ordinal))
                    .Select(label => observation.Gestor + "/" +
                        (observation.Cpf is null ? "WITHOUT_CPF" : "WITH_CPF") + "/" + label))
                .GroupBy(label => label, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            annotated["brazilian_name_errors_realized_label_counts"] = JsonSerializer.SerializeToNode(realized);
            return JsonSerializer.Serialize(annotated, IndentedJsonOptions) + "\n";
        }
        return truthJson;
    }

    private static void AppendCsvRow(StringBuilder builder, params string?[] fields)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0)
                builder.Append(',');
            builder.Append(EscapeCsv(fields[i] ?? string.Empty));
        }
        builder.Append('\n');
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string FormatWeight(double value)
        => value.ToString("0.########", CultureInfo.InvariantCulture);

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
