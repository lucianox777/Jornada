using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests;

[TestFixture]
public sealed class LinkageRunBlockingProvenanceTests
{
    private static readonly Guid ModelId = Guid.Parse("91000000-0000-4000-8000-000000000012");
    private static readonly Guid CorrelationId = Guid.Parse("92000000-0000-4000-8000-000000000001");
    private const string RuleSetVersion = "MODEL_12_BLOCKING_V1";
    private const string RuleSetFingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ProjectionSchemaVersion = "PERSON_RESOLUTION_PROJECTION_V2";
    private const string ProjectionFingerprint = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Test]
    public void RuntimeSnapshot_ExportsExactBlockingContract()
    {
        var model = new LinkageModel(
            ModelId,
            12,
            "FELLEGI_SUNTER_V1",
            new Dictionary<string, decimal>(),
            0.90m,
            0.05m);
        var ruleSet = new LinkageDynamicRuleSet(
            RuleSetVersion,
            "FELLEGI_SUNTER_V1",
            new[] { "nome_completo__normalized" },
            new Dictionary<string, decimal>(),
            null,
            null,
            RuleSetFingerprint)
        {
            ProjectionSchemaVersion = ProjectionSchemaVersion,
            ProjectionFingerprintSha256 = ProjectionFingerprint
        };

        var reference = new LinkageRuntimeSnapshot(model, ruleSet).Reference;

        Assert.Multiple(() =>
        {
            Assert.That(reference.ModelId, Is.EqualTo(ModelId));
            Assert.That(reference.Version, Is.EqualTo(12));
            Assert.That(reference.AlgorithmVersion, Is.EqualTo("FELLEGI_SUNTER_V1"));
            Assert.That(reference.BlockingContract, Is.Not.Null);
            Assert.That(reference.BlockingContract!.RuleSetVersion, Is.EqualTo(RuleSetVersion));
            Assert.That(reference.BlockingContract.RuleSetFingerprintSha256, Is.EqualTo(RuleSetFingerprint));
            Assert.That(reference.BlockingContract.ProjectionSchemaVersion, Is.EqualTo(ProjectionSchemaVersion));
            Assert.That(reference.BlockingContract.ProjectionFingerprintSha256, Is.EqualTo(ProjectionFingerprint));
        });
    }

    [Test]
    public void ScopeJson_RecordsSelectedModelAndRuleSetProvenance()
    {
        var request = Request();
        var model = ModelReference() with
        {
            BlockingContract = new ProbabilisticLinkageBlockingContractRef(
                RuleSetVersion,
                RuleSetFingerprint,
                ProjectionSchemaVersion,
                ProjectionFingerprint)
        };

        using var document = JsonDocument.Parse(ProbabilisticLinkageBatchRunner.BuildScopeJson(request, model));
        var root = document.RootElement;
        var selectedModel = root.GetProperty("selectedModel");
        var blocking = root.GetProperty("blocking");

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("mode").GetString(), Is.EqualTo("REPLAY"));
            Assert.That(root.GetProperty("modelVersion").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(root.GetProperty("pessoaObservacaoId").GetInt64(), Is.EqualTo(123));
            Assert.That(root.GetProperty("gestorCodigo").GetString(), Is.EqualTo("SMADS"));
            Assert.That(root.GetProperty("maxRecords").GetInt64(), Is.EqualTo(5000));
            Assert.That(root.GetProperty("publish").GetBoolean(), Is.False);

            Assert.That(selectedModel.GetProperty("modelId").GetGuid(), Is.EqualTo(ModelId));
            Assert.That(selectedModel.GetProperty("version").GetInt32(), Is.EqualTo(12));
            Assert.That(selectedModel.GetProperty("algorithmVersion").GetString(), Is.EqualTo("FELLEGI_SUNTER_V1"));

            Assert.That(blocking.GetProperty("mode").GetString(), Is.EqualTo("RULESET"));
            Assert.That(blocking.GetProperty("ruleSetVersion").GetString(), Is.EqualTo(RuleSetVersion));
            Assert.That(blocking.GetProperty("ruleSetFingerprintSha256").GetString(), Is.EqualTo(RuleSetFingerprint));
            Assert.That(blocking.GetProperty("projectionSchemaVersion").GetString(), Is.EqualTo(ProjectionSchemaVersion));
            Assert.That(blocking.GetProperty("projectionFingerprintSha256").GetString(), Is.EqualTo(ProjectionFingerprint));
        });
    }

    [Test]
    public void ScopeJson_MarksLegacyBlockingExplicitly()
    {
        var model = ModelReference();

        Assert.That(model.BlockingContract, Is.Null, "O construtor histórico de cinco argumentos deve permanecer compatível.");

        using var document = JsonDocument.Parse(ProbabilisticLinkageBatchRunner.BuildScopeJson(Request(), model));
        var blocking = document.RootElement.GetProperty("blocking");
        var selectedModel = document.RootElement.GetProperty("selectedModel");

        Assert.Multiple(() =>
        {
            Assert.That(blocking.GetProperty("mode").GetString(), Is.EqualTo("LEGACY"));
            Assert.That(blocking.GetProperty("ruleSetVersion").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(blocking.GetProperty("ruleSetFingerprintSha256").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(blocking.GetProperty("projectionSchemaVersion").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(blocking.GetProperty("projectionFingerprintSha256").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(selectedModel.GetProperty("modelId").GetGuid(), Is.EqualTo(ModelId));
            Assert.That(selectedModel.GetProperty("version").GetInt32(), Is.EqualTo(12));
        });
    }

    private static ProbabilisticLinkageModelRef ModelReference() =>
        new(ModelId, 12, "FELLEGI_SUNTER_V1", 0.90m, 0.05m);

    private static ProbabilisticLinkageRunRequest Request() =>
        new(
            LinkageRunType.REPLAY,
            null,
            123,
            "SMADS",
            new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(-3)),
            1000,
            4,
            5000,
            "ci",
            "audit",
            CorrelationId,
            false);
}
