using System.Text.Json;
using System.Text.Json.Nodes;
using Jornada.Contracts;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class LinkageCalibrationAuditRoundTripTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Test]
    public void Typed_document_roundtrips_without_semantic_loss()
    {
        var expected = SampleDocument();
        var json = JsonSerializer.Serialize(expected, JsonOptions);

        var actual = LinkageCalibrationAuditRoundTrip.Import(json);

        Assert.DoesNotThrow(() =>
            LinkageCalibrationAuditRoundTrip.VerifyEquivalent(expected, actual));
    }

    [Test]
    public void Roundtrip_reports_exact_parameter_path_on_divergence()
    {
        var expected = SampleDocument();
        var changedParameters = expected.Parameters.ToArray();
        changedParameters[0] = changedParameters[0] with
        {
            Value = changedParameters[0].Value + 0.01m
        };
        var changed = expected with { Parameters = changedParameters };

        var ex = Assert.Throws<InvalidDataException>(() =>
            LinkageCalibrationAuditRoundTrip.VerifyEquivalent(expected, changed));

        Assert.That(ex!.Message, Does.Contain("parameters[0]"));
    }

    [Test]
    public void Import_rejects_unknown_members_instead_of_silently_losing_them()
    {
        var json = JsonSerializer.Serialize(SampleDocument(), JsonOptions);
        var root = JsonNode.Parse(json)!.AsObject();
        root["cpf"] = "12345678901";

        var ex = Assert.Throws<JsonException>(() =>
            LinkageCalibrationAuditRoundTrip.Import(root.ToJsonString()));

        Assert.That(ex!.Message, Does.Contain("cpf"));
    }

    [Test]
    public void Import_rejects_status_or_semantic_contract_divergence()
    {
        var sample = SampleDocument();

        var wrongStatus = sample with
        {
            InterchangeContract = sample.InterchangeContract with { StatusAtExport = "ATIVO" }
        };
        var statusEx = Assert.Throws<InvalidDataException>(() =>
            LinkageCalibrationAuditRoundTrip.Import(JsonSerializer.Serialize(wrongStatus, JsonOptions)));

        var wrongStates = sample with
        {
            InterchangeContract = sample.InterchangeContract with
            {
                ComparisonStateMapping = sample.InterchangeContract.ComparisonStateMapping with
                {
                    UnmappedOrNonBijectiveStates = ["DAY_MONTH_SWAP"]
                }
            }
        };
        var statesEx = Assert.Throws<InvalidDataException>(() =>
            LinkageCalibrationAuditRoundTrip.Import(JsonSerializer.Serialize(wrongStates, JsonOptions)));

        var wrongRule = sample with
        {
            InterchangeContract = sample.InterchangeContract with
            {
                ComparisonStateMapping = sample.InterchangeContract.ComparisonStateMapping with
                {
                    Rule = "silently collapse states"
                }
            }
        };
        var ruleEx = Assert.Throws<InvalidDataException>(() =>
            LinkageCalibrationAuditRoundTrip.Import(JsonSerializer.Serialize(wrongRule, JsonOptions)));

        Assert.Multiple(() =>
        {
            Assert.That(statusEx!.Message, Does.Contain("statusAtExport"));
            Assert.That(statesEx!.Message, Does.Contain("Estados não bijetivos"));
            Assert.That(ruleEx!.Message, Does.Contain("Regra de mapeamento"));
        });
    }

    [Test]
    public void Import_rejects_nominal_u_source_that_disagrees_with_parameters()
    {
        var sample = SampleDocument();
        var wrong = sample with
        {
            InterchangeContract = sample.InterchangeContract with
            {
                NominalNameUSource = LinkageCalibrationAuditExchangePolicy.NominalUSourceIbgeBootstrap
            }
        };

        var ex = Assert.Throws<InvalidDataException>(() =>
            LinkageCalibrationAuditRoundTrip.Import(JsonSerializer.Serialize(wrong, JsonOptions)));

        Assert.That(ex!.Message, Does.Contain("Fonte nominal de u para nome"));
    }

    [Test]
    public void Import_rejects_runtime_tf_or_reference_provenance_divergence()
    {
        var sample = SampleDocument();

        var runtimeTf = sample with
        {
            TermFrequency = sample.TermFrequency with { RuntimeEnabled = true }
        };
        var tfEx = Assert.Throws<InvalidDataException>(() =>
            LinkageCalibrationAuditRoundTrip.Import(JsonSerializer.Serialize(runtimeTf, JsonOptions)));

        var wrongAlgorithm = sample with
        {
            TermFrequency = sample.TermFrequency with { AlgorithmVersion = "TF_UNKNOWN" }
        };
        var algorithmEx = Assert.Throws<InvalidDataException>(() =>
            LinkageCalibrationAuditRoundTrip.Import(JsonSerializer.Serialize(wrongAlgorithm, JsonOptions)));

        var wrongReference = sample with
        {
            TermFrequency = sample.TermFrequency with
            {
                ReferenceSnapshot = sample.TermFrequency.ReferenceSnapshot! with { VersionId = 999 }
            }
        };
        var referenceEx = Assert.Throws<InvalidDataException>(() =>
            LinkageCalibrationAuditRoundTrip.Import(JsonSerializer.Serialize(wrongReference, JsonOptions)));

        Assert.Multiple(() =>
        {
            Assert.That(tfEx!.Message, Does.Contain("term frequency habilitada"));
            Assert.That(algorithmEx!.Message, Does.Contain("Versão da matemática"));
            Assert.That(referenceEx!.Message, Does.Contain("versão fixada no modelo"));
        });
    }

    [Test]
    public void Import_rejects_unpromoted_model_even_when_json_is_well_formed()
    {
        var draft = SampleDocument() with
        {
            Model = SampleDocument().Model with { Status = "RASCUNHO" },
            InterchangeContract = SampleDocument().InterchangeContract with { StatusAtExport = "RASCUNHO" }
        };
        var json = JsonSerializer.Serialize(draft, JsonOptions);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            LinkageCalibrationAuditRoundTrip.Import(json));

        Assert.That(ex!.Message, Does.Contain("ATIVO ou VALIDADO"));
    }

    private static LinkageCalibrationAuditDocument SampleDocument()
    {
        var modelId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var ruleSetId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var generated = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

        return new LinkageCalibrationAuditDocument(
            SchemaVersion: LinkageCalibrationAuditExchangePolicy.SchemaVersion,
            Nature: LinkageCalibrationAuditExchangePolicy.Nature,
            Purpose: LinkageCalibrationAuditExchangePolicy.Purpose,
            GeneratedAtUtc: generated.AddMinutes(1),
            Safeguards: ["read-only SELECTs only"],
            Model: new LinkageCalibrationAuditModel(
                ModelId: modelId,
                Version: 8,
                Status: "VALIDADO",
                AlgorithmVersion: "FELLEGI_SUNTER_DECISION_EVIDENCE_V6",
                NormalizationVersion: "IDENTITY_NORMALIZATION_V1",
                DeduplicationMethod: "GOLD_PESSOA_UUID_PK",
                BaseReference: "gold.pessoa",
                SnapshotReference: "snapshot-8",
                RecordsRead: 1000,
                UniquePeople: 1000,
                GeneratedAt: generated,
                ActivatedAt: null,
                SnapshotCapturedAt: new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Unspecified),
                SampleMethod: "M_INTERGESTOR_U_BLOCKING_CONDITIONED_IBGE_BOOTSTRAP_V5",
                SamplePoolSize: 5000,
                SampleMSize: 800,
                SampleUSize: 1200,
                FailureSummary: null,
                NameFrequencyVersionId: 4),
            Parameters:
            [
                new("T_LINKAGE", 0.91m),
                new("NOMINAL_U_NOME_SOURCE_BLOCKING_CONDITIONED", 1m),
                new("IBGE_MC_NOMINAL_U_APPLIED_NOME", 0m),
                new("NOMINAL_U_NOME_MAE_SOURCE_BLOCKING_CONDITIONED", 0m),
                new("IBGE_MC_NOMINAL_U_APPLIED_NOME_MAE", 1m)
            ],
            Statistics: [new("POPULATION_SIZE", 1000m, "SQL_SERVER")],
            InterchangeContract: new LinkageCalibrationAuditInterchangeContract(
                StatusAtExport: "VALIDADO",
                UProbabilitySemantics: LinkageCalibrationAuditExchangePolicy.UProbabilitySemantics,
                NominalNameUSource: LinkageCalibrationAuditExchangePolicy.NominalUSourceBlockingConditioned,
                NominalMotherNameUSource: LinkageCalibrationAuditExchangePolicy.NominalUSourceIbgeBootstrap,
                SplinkDefaultRandomPairUEquivalent: false,
                ComparisonStateMapping: new LinkageCalibrationAuditComparisonMapping(
                    Complete: false,
                    UnmappedOrNonBijectiveStates:
                        LinkageCalibrationAuditExchangePolicy.UnmappedOrNonBijectiveComparisonStates.ToArray(),
                    Rule: LinkageCalibrationAuditExchangePolicy.ComparisonStateMappingRule)),
            Blocking: new LinkageCalibrationAuditBlocking(
                RuleSets:
                [
                    new(
                        RuleSetId: ruleSetId,
                        RuleSetVersion: "RULESET_V1",
                        AlgorithmVersion: "BLOCKING_RULESET_V1",
                        FingerprintSha256: new string('a', 64),
                        IbgeSourceVersion: "IBGE-2022",
                        IbgeFingerprintSha256: new string('b', 64),
                        CreatedAt: generated)
                ],
                Passes:
                [
                    new(
                        RuleSetId: ruleSetId,
                        Order: 0,
                        PassId: "NOME_NASCIMENTO",
                        Attributes: ["NOME", "DATA_NASCIMENTO"])
                ]),
            TermFrequency: new LinkageCalibrationAuditTermFrequency(
                RuntimeEnabled: false,
                AlgorithmVersion: SplinkCompatibleTermFrequency.AlgorithmVersion,
                PersistedModelFrequencyRows: 0,
                ReferenceSnapshot: new LinkageCalibrationAuditFrequencyReference(
                    VersionId: 4,
                    Code: "IBGE-2022",
                    Source: "IBGE",
                    Edition: "2022",
                    ReferenceDate: new DateOnly(2022, 1, 1),
                    PublishedAt: new DateOnly(2023, 1, 1),
                    Status: "ATIVA",
                    ContentSha256: new string('c', 64),
                    CreatedAt: generated.AddYears(-1),
                    ActivatedAt: generated.AddMonths(-1)),
                ReferenceCoverage:
                [
                    new("NOME", "BRASIL", "TODOS", "TODOS", 100, 1_000_000m)
                ],
                ConformanceVectors:
                [
                    new("rare-rare", 0.001m, 0.001m, 0.05m, 1m, 0.000001m, 0.001m, 3.912023005428146)
                ],
                Interpretation: "TF não habilitada no runtime."));
    }
}
