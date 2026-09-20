using System.Text.Json;
using System.Text.Json.Serialization;

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
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static LinkageCalibrationAuditDocument Import(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var document = JsonSerializer.Deserialize<LinkageCalibrationAuditDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("Documento de auditoria de calibração vazio ou incompatível.");

        if (document.SchemaVersion != LinkageCalibrationAuditExchangePolicy.SchemaVersion)
            throw new InvalidDataException($"schemaVersion de auditoria não suportada: {document.SchemaVersion}.");
        if (!string.Equals(document.Nature, LinkageCalibrationAuditExchangePolicy.Nature, StringComparison.Ordinal))
            throw new InvalidDataException($"nature inesperada: {document.Nature}.");
        if (!string.Equals(document.Purpose, LinkageCalibrationAuditExchangePolicy.Purpose, StringComparison.Ordinal))
            throw new InvalidDataException($"purpose inesperado: {document.Purpose}.");

        if (document.Model is null
            || document.InterchangeContract is null
            || document.InterchangeContract.ComparisonStateMapping is null
            || document.Blocking is null
            || document.TermFrequency is null
            || document.Parameters is null
            || document.Statistics is null
            || document.Safeguards is null)
            throw new InvalidDataException("Documento de auditoria incompleto.");

        if (document.Model.ModelId == Guid.Empty || document.Model.Version <= 0)
            throw new InvalidDataException("Identidade/versão do modelo de auditoria inválida.");

        LinkageCalibrationAuditExchangePolicy.EnsureExportableModelStatus(
            document.Model.ModelId,
            document.Model.Status);

        if (!string.Equals(
                document.InterchangeContract.StatusAtExport,
                document.Model.Status,
                StringComparison.Ordinal))
            throw new InvalidDataException("statusAtExport diverge do status persistido do modelo.");

        if (!string.Equals(
                document.InterchangeContract.UProbabilitySemantics,
                LinkageCalibrationAuditExchangePolicy.UProbabilitySemantics,
                StringComparison.Ordinal))
            throw new InvalidDataException("uProbabilitySemantics diverge do contrato corrente.");
        if (document.InterchangeContract.SplinkDefaultRandomPairUEquivalent)
            throw new InvalidDataException("O documento não pode declarar equivalência ao u aleatório padrão do Splink.");
        if (document.InterchangeContract.ComparisonStateMapping.Complete)
            throw new InvalidDataException("O contrato corrente não possui mapeamento completo dos estados semânticos.");

        if (document.InterchangeContract.ComparisonStateMapping.UnmappedOrNonBijectiveStates is null)
            throw new InvalidDataException("Lista de estados não bijetivos ausente.");

        if (!document.InterchangeContract.ComparisonStateMapping.UnmappedOrNonBijectiveStates
                .SequenceEqual(LinkageCalibrationAuditExchangePolicy.UnmappedOrNonBijectiveComparisonStates, StringComparer.Ordinal))
            throw new InvalidDataException("Estados não bijetivos do intercâmbio divergem do contrato corrente.");

        if (!string.Equals(
                document.InterchangeContract.ComparisonStateMapping.Rule,
                LinkageCalibrationAuditExchangePolicy.ComparisonStateMappingRule,
                StringComparison.Ordinal))
            throw new InvalidDataException("Regra de mapeamento semântico diverge do contrato corrente.");

        if (document.Blocking.RuleSets is null
            || document.Blocking.Passes is null
            || document.TermFrequency.ReferenceCoverage is null
            || document.TermFrequency.ConformanceVectors is null)
            throw new InvalidDataException("Coleções estruturais obrigatórias ausentes.");

        if (document.TermFrequency.RuntimeEnabled)
            throw new InvalidDataException("O artefato de auditoria não pode declarar term frequency habilitada no runtime.");

        if (document.Model.NameFrequencyVersionId is long pinnedReferenceId)
        {
            if (document.TermFrequency.ReferenceSnapshot is null)
                throw new InvalidDataException("Modelo fixa referência nominal, mas o snapshot correspondente não foi exportado.");
            if (document.TermFrequency.ReferenceSnapshot.VersionId != pinnedReferenceId)
                throw new InvalidDataException("Snapshot nominal exportado diverge da versão fixada no modelo.");
        }
        else if (document.TermFrequency.ReferenceSnapshot is not null)
        {
            throw new InvalidDataException("Snapshot nominal exportado sem versão correspondente fixada no modelo.");
        }

        var ruleSetIds = document.Blocking.RuleSets.Select(x => x.RuleSetId).ToHashSet();
        if (document.Blocking.Passes.Any(x => !ruleSetIds.Contains(x.RuleSetId)))
            throw new InvalidDataException("Passe de blocking referencia ruleset ausente do documento.");

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
