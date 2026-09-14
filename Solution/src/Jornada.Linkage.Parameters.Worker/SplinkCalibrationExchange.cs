using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record SplinkCalibrationRecord(
    [property: JsonPropertyName("unique_id")] string UniqueId,
    [property: JsonPropertyName("source_dataset")] string SourceDataset,
    [property: JsonPropertyName("base_person_id")] string BasePersonId,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("surname")] string Surname,
    [property: JsonPropertyName("dob")] string? BirthDate);

public sealed record SplinkPairwiseLabel(
    [property: JsonPropertyName("source_dataset_l")] string SourceDatasetLeft,
    [property: JsonPropertyName("unique_id_l")] string UniqueIdLeft,
    [property: JsonPropertyName("source_dataset_r")] string SourceDatasetRight,
    [property: JsonPropertyName("unique_id_r")] string UniqueIdRight,
    [property: JsonPropertyName("clerical_match_score")] decimal ClericalMatchScore);

public sealed record SplinkCalibrationPackage(
    string SchemaVersion,
    string GeneratorVersion,
    string IbgeSourceVersion,
    string IbgeFingerprintSha256,
    int Seed,
    BenchmarkPartition Partition,
    IReadOnlyList<SplinkCalibrationRecord> Records,
    IReadOnlyList<SplinkPairwiseLabel> Labels);

public sealed record SplinkComparisonLevelEstimate(
    [property: JsonPropertyName("feature")] string Feature,
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("m_probability")] decimal? MProbability,
    [property: JsonPropertyName("u_probability")] decimal? UProbability);

public sealed record SplinkRunnerResult(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("source_schema_version")] string SourceSchemaVersion,
    [property: JsonPropertyName("splink_version")] string SplinkVersion,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("nominal_semantics_version")] string NominalSemanticsVersion,
    [property: JsonPropertyName("seed")] int Seed,
    [property: JsonPropertyName("max_pairs")] long MaxPairs,
    [property: JsonPropertyName("estimates")] IReadOnlyList<SplinkComparisonLevelEstimate> Estimates);

/// <summary>
/// Fronteira explícita Jornada -> Splink. O domínio Jornada permanece soberano;
/// este tradutor materializa apenas o formato de intercâmbio necessário para treino,
/// avaliação e importação versionada de estimativas.
/// </summary>
public static class SplinkCalibrationExchange
{
    public const string SchemaVersion = "JORNADA_SPLINK_EXCHANGE_V1";
    public const string RunnerSchemaVersion = "JORNADA_SPLINK_ESTIMATES_V1";
    public const string NominalSemanticsVersion = "IDENTITY_NAME_STATES_V1";
    public const string SourceDataset = "jornada_calibrador";

    private static readonly JsonSerializerOptions ExchangeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public static SplinkCalibrationPackage Export(
        IbgeTypedNameFrequencySnapshot snapshot,
        IEnumerable<IbgeNominalBenchmarkPair> pairs,
        BenchmarkPartition partition,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(pairs);

        var selected = pairs
            .Where(pair => pair.Partition == partition)
            .OrderBy(static pair => pair.PairId, StringComparer.Ordinal)
            .ToArray();

        var records = new List<SplinkCalibrationRecord>(selected.Length * 2);
        var labels = new List<SplinkPairwiseLabel>(selected.Length);

        foreach (var pair in selected)
        {
            var leftId = pair.PairId + ":L";
            var rightId = pair.PairId + ":R";
            records.Add(ToRecord(leftId, pair.Left));
            records.Add(ToRecord(rightId, pair.Right));
            labels.Add(new SplinkPairwiseLabel(
                SourceDataset,
                leftId,
                SourceDataset,
                rightId,
                pair.IsTrueMatch ? 1m : 0m));
        }

        return new SplinkCalibrationPackage(
            SchemaVersion,
            IbgeNominalBenchmarkOptions.GeneratorVersion,
            snapshot.SourceVersion,
            snapshot.FingerprintSha256,
            seed,
            partition,
            records,
            labels);
    }

    public static string Serialize(SplinkCalibrationPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return JsonSerializer.Serialize(package, ExchangeJsonOptions);
    }

    public static SplinkRunnerResult DeserializeRunnerResult(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var result = JsonSerializer.Deserialize<SplinkRunnerResult>(json)
            ?? throw new InvalidOperationException("Resultado Splink vazio ou inválido.");

        if (!string.Equals(result.SchemaVersion, RunnerSchemaVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Schema do runner incompatível: {result.SchemaVersion}.");
        if (!string.Equals(result.SourceSchemaVersion, SchemaVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Schema de origem incompatível: {result.SourceSchemaVersion}.");
        if (!string.Equals(result.Scope, "NOME", StringComparison.Ordinal))
            throw new InvalidOperationException($"Escopo Splink V1 incompatível: {result.Scope}.");
        if (!string.Equals(result.NominalSemanticsVersion, NominalSemanticsVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Semântica nominal incompatível: {result.NominalSemanticsVersion}.");
        if (string.IsNullOrWhiteSpace(result.SplinkVersion))
            throw new InvalidOperationException("Versão do Splink ausente.");
        if (result.MaxPairs <= 0)
            throw new InvalidOperationException("max_pairs inválido no resultado Splink.");
        if (result.Estimates.Count == 0)
            throw new InvalidOperationException("Resultado Splink sem estimativas.");

        return result;
    }

    public static ParameterEstimate ImportM(SplinkRunnerResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return ImportM(result.SplinkVersion, result.Estimates);
    }

    public static ParameterEstimate ImportU(SplinkRunnerResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return ImportU(result.SplinkVersion, result.Estimates);
    }

    public static ParameterEstimate ImportM(
        string splinkVersion,
        IEnumerable<SplinkComparisonLevelEstimate> estimates) =>
        Import(ParameterEstimatorKind.Splink, splinkVersion, estimates, importM: true);

    public static ParameterEstimate ImportU(
        string splinkVersion,
        IEnumerable<SplinkComparisonLevelEstimate> estimates) =>
        Import(ParameterEstimatorKind.Splink, splinkVersion, estimates, importM: false);

    private static ParameterEstimate Import(
        ParameterEstimatorKind estimator,
        string version,
        IEnumerable<SplinkComparisonLevelEstimate> estimates,
        bool importM)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(estimates);

        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var estimate in estimates)
        {
            var feature = NormalizeKeyPart(estimate.Feature, nameof(estimate.Feature));
            var level = NormalizeKeyPart(estimate.Level, nameof(estimate.Level));
            var probability = importM ? estimate.MProbability : estimate.UProbability;
            if (!probability.HasValue)
                continue;
            if (probability.Value <= 0m || probability.Value >= 1m)
                throw new ArgumentOutOfRangeException(nameof(estimates), "Probabilidades importadas devem estar em (0,1).");

            var key = $"{(importM ? "M" : "U")}_{feature}_{level}";
            if (!values.TryAdd(key, probability.Value))
                throw new ArgumentException($"Estimativa duplicada para {key}.", nameof(estimates));
        }

        if (values.Count == 0)
            throw new ArgumentException("Nenhuma estimativa utilizável foi importada.", nameof(estimates));

        return new ParameterEstimate(estimator, version.Trim(), values);
    }

    private static SplinkCalibrationRecord ToRecord(string uniqueId, IbgeBenchmarkPerson person) =>
        new(
            uniqueId,
            SourceDataset,
            person.BasePersonId,
            person.FirstName,
            person.Surname,
            person.BirthDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

    private static string NormalizeKeyPart(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Valor obrigatório.", parameterName);

        var normalized = new string(value.Trim().ToUpperInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray());
        while (normalized.Contains("__", StringComparison.Ordinal))
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        return normalized.Trim('_');
    }
}
