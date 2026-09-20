using System.Text.Json;

namespace Jornada.Contracts;

public sealed record LinkageCalibrationAuditDocument(
    int SchemaVersion,
    string Nature,
    string Purpose,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<string> Safeguards,
    LinkageCalibrationAuditModel Model,
    IReadOnlyList<LinkageCalibrationAuditParameter> Parameters,
    IReadOnlyList<LinkageCalibrationAuditStatistic> Statistics,
    LinkageCalibrationAuditInterchangeContract InterchangeContract,
    LinkageCalibrationAuditBlocking Blocking,
    LinkageCalibrationAuditTermFrequency TermFrequency);

public sealed record LinkageCalibrationAuditModel(
    Guid ModelId,
    int Version,
    string Status,
    string AlgorithmVersion,
    string NormalizationVersion,
    string DeduplicationMethod,
    string BaseReference,
    string? SnapshotReference,
    long? RecordsRead,
    long? UniquePeople,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? ActivatedAt,
    DateTime? SnapshotCapturedAt,
    string? SampleMethod,
    int? SamplePoolSize,
    int? SampleMSize,
    int? SampleUSize,
    string? FailureSummary,
    long? NameFrequencyVersionId);

public sealed record LinkageCalibrationAuditParameter(string Name, decimal Value);
public sealed record LinkageCalibrationAuditStatistic(string Name, decimal Value, string Method);

public sealed record LinkageCalibrationAuditRuleSet(
    Guid RuleSetId,
    string RuleSetVersion,
    string AlgorithmVersion,
    string FingerprintSha256,
    string? IbgeSourceVersion,
    string? IbgeFingerprintSha256,
    DateTimeOffset CreatedAt);

public sealed record LinkageCalibrationAuditPass(
    Guid RuleSetId,
    int Order,
    string PassId,
    IReadOnlyList<string> Attributes);

public sealed record LinkageCalibrationAuditBlocking(
    IReadOnlyList<LinkageCalibrationAuditRuleSet> RuleSets,
    IReadOnlyList<LinkageCalibrationAuditPass> Passes);

public sealed record LinkageCalibrationAuditComparisonMapping(
    bool Complete,
    IReadOnlyList<string> UnmappedOrNonBijectiveStates,
    string Rule);

public sealed record LinkageCalibrationAuditInterchangeContract(
    string StatusAtExport,
    string UProbabilitySemantics,
    bool SplinkDefaultRandomPairUEquivalent,
    LinkageCalibrationAuditComparisonMapping ComparisonStateMapping);

public sealed record LinkageCalibrationAuditFrequencyReference(
    long VersionId,
    string Code,
    string Source,
    string Edition,
    DateOnly ReferenceDate,
    DateOnly? PublishedAt,
    string Status,
    string? ContentSha256,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ActivatedAt);

public sealed record LinkageCalibrationAuditFrequencyCoverage(
    string Type,
    string GeographicScope,
    string Sex,
    string BirthPeriod,
    long Records,
    decimal FrequencySum);

public sealed record LinkageCalibrationAuditTfVector(
    string Name,
    decimal LeftFrequency,
    decimal RightFrequency,
    decimal ReferenceUProbability,
    decimal Weight,
    decimal MinimumUValue,
    decimal EffectiveFrequency,
    double LogBayesAdjustmentNatural);

public sealed record LinkageCalibrationAuditTermFrequency(
    bool RuntimeEnabled,
    string AlgorithmVersion,
    long PersistedModelFrequencyRows,
    LinkageCalibrationAuditFrequencyReference? ReferenceSnapshot,
    IReadOnlyList<LinkageCalibrationAuditFrequencyCoverage> ReferenceCoverage,
    IReadOnlyList<LinkageCalibrationAuditTfVector> ConformanceVectors,
    string Interpretation);

public static class LinkageCalibrationAuditRoundTrip
{
    public const string MethodVersion = "JORNADA_CALIBRATION_AUDIT_ROUNDTRIP_V1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static LinkageCalibrationAuditDocument Import(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var document = JsonSerializer.Deserialize<LinkageCalibrationAuditDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("Documento de auditoria de calibração vazio ou incompatível.");

        if (document.SchemaVersion != 1)
            throw new InvalidDataException($"schemaVersion de auditoria não suportada: {document.SchemaVersion}.");
        if (!string.Equals(document.Nature, "LINKAGE_CALIBRATION_AUDIT_EXPORT", StringComparison.Ordinal))
            throw new InvalidDataException($"nature inesperada: {document.Nature}.");
        if (!string.Equals(document.Purpose, "EXTERNAL_REPRODUCIBILITY_READ_ONLY", StringComparison.Ordinal))
            throw new InvalidDataException($"purpose inesperado: {document.Purpose}.");

        LinkageCalibrationAuditExchangePolicy.EnsureExportableModelStatus(
            document.Model.ModelId,
            document.Model.Status);

        if (!string.Equals(
                document.InterchangeContract.UProbabilitySemantics,
                LinkageCalibrationAuditExchangePolicy.UProbabilitySemantics,
                StringComparison.Ordinal))
            throw new InvalidDataException("uProbabilitySemantics diverge do contrato corrente.");
        if (document.InterchangeContract.SplinkDefaultRandomPairUEquivalent)
            throw new InvalidDataException("O documento não pode declarar equivalência ao u aleatório padrão do Splink.");
        if (document.InterchangeContract.ComparisonStateMapping.Complete)
            throw new InvalidDataException("O contrato corrente não possui mapeamento completo dos estados semânticos.");

        return document;
    }

    public static void VerifyEquivalent(
        LinkageCalibrationAuditDocument expected,
        LinkageCalibrationAuditDocument actual)
    {
        Equal("schemaVersion", expected.SchemaVersion, actual.SchemaVersion);
        Equal("nature", expected.Nature, actual.Nature);
        Equal("purpose", expected.Purpose, actual.Purpose);
        Equal("generatedAtUtc", expected.GeneratedAtUtc, actual.GeneratedAtUtc);
        Sequence("safeguards", expected.Safeguards, actual.Safeguards);

        Equal("model", expected.Model, actual.Model);
        Sequence("parameters", expected.Parameters, actual.Parameters);
        Sequence("statistics", expected.Statistics, actual.Statistics);

        Equal("interchangeContract.statusAtExport",
            expected.InterchangeContract.StatusAtExport,
            actual.InterchangeContract.StatusAtExport);
        Equal("interchangeContract.uProbabilitySemantics",
            expected.InterchangeContract.UProbabilitySemantics,
            actual.InterchangeContract.UProbabilitySemantics);
        Equal("interchangeContract.splinkDefaultRandomPairUEquivalent",
            expected.InterchangeContract.SplinkDefaultRandomPairUEquivalent,
            actual.InterchangeContract.SplinkDefaultRandomPairUEquivalent);
        Equal("interchangeContract.comparisonStateMapping.complete",
            expected.InterchangeContract.ComparisonStateMapping.Complete,
            actual.InterchangeContract.ComparisonStateMapping.Complete);
        Sequence(
            "interchangeContract.comparisonStateMapping.unmappedOrNonBijectiveStates",
            expected.InterchangeContract.ComparisonStateMapping.UnmappedOrNonBijectiveStates,
            actual.InterchangeContract.ComparisonStateMapping.UnmappedOrNonBijectiveStates);
        Equal("interchangeContract.comparisonStateMapping.rule",
            expected.InterchangeContract.ComparisonStateMapping.Rule,
            actual.InterchangeContract.ComparisonStateMapping.Rule);

        Sequence("blocking.ruleSets", expected.Blocking.RuleSets, actual.Blocking.RuleSets);
        Passes(expected.Blocking.Passes, actual.Blocking.Passes);

        Equal("termFrequency.runtimeEnabled",
            expected.TermFrequency.RuntimeEnabled,
            actual.TermFrequency.RuntimeEnabled);
        Equal("termFrequency.algorithmVersion",
            expected.TermFrequency.AlgorithmVersion,
            actual.TermFrequency.AlgorithmVersion);
        Equal("termFrequency.persistedModelFrequencyRows",
            expected.TermFrequency.PersistedModelFrequencyRows,
            actual.TermFrequency.PersistedModelFrequencyRows);
        Equal("termFrequency.referenceSnapshot",
            expected.TermFrequency.ReferenceSnapshot,
            actual.TermFrequency.ReferenceSnapshot);
        Sequence("termFrequency.referenceCoverage",
            expected.TermFrequency.ReferenceCoverage,
            actual.TermFrequency.ReferenceCoverage);
        Sequence("termFrequency.conformanceVectors",
            expected.TermFrequency.ConformanceVectors,
            actual.TermFrequency.ConformanceVectors);
        Equal("termFrequency.interpretation",
            expected.TermFrequency.Interpretation,
            actual.TermFrequency.Interpretation);
    }

    private static void Passes(
        IReadOnlyList<LinkageCalibrationAuditPass> expected,
        IReadOnlyList<LinkageCalibrationAuditPass> actual)
    {
        Equal("blocking.passes.count", expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Equal($"blocking.passes[{i}].ruleSetId", expected[i].RuleSetId, actual[i].RuleSetId);
            Equal($"blocking.passes[{i}].order", expected[i].Order, actual[i].Order);
            Equal($"blocking.passes[{i}].passId", expected[i].PassId, actual[i].PassId);
            Sequence($"blocking.passes[{i}].attributes", expected[i].Attributes, actual[i].Attributes);
        }
    }

    private static void Sequence<T>(string path, IReadOnlyList<T> expected, IReadOnlyList<T> actual)
    {
        Equal(path + ".count", expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
            Equal($"{path}[{i}]", expected[i], actual[i]);
    }

    private static void Equal<T>(string path, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidDataException(
                $"Round-trip divergente em {path}: esperado={expected}; atual={actual}.");
    }
}
