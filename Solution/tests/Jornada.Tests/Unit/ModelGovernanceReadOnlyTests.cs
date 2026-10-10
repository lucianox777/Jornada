using Jornada.Api;
using NUnit.Framework;
using System.Text.Json;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ModelGovernanceReadOnlyTests
{
    private static readonly Guid ActiveId =
        Guid.Parse("a1111111-1111-4111-8111-111111111111");

    [Test]
    public void View_ExposesOnlyPairedBlockingTrainingWhenBaselineIsCurrent()
    {
        var result = ModelGovernanceReadOnlyService.EvaluateEvidence(
            Measurements(ActiveId.ToString("N")), Active());
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.Status, Is.EqualTo("COMPARAVEL_APENAS_BLOCKING_TREINO"));
            Assert.That(result.MatchedPairWeight, Is.EqualTo(30m));
            Assert.That(result.NonMatchedPairWeight, Is.EqualTo(70m));
            Assert.That(result.ActiveRecall, Is.EqualTo(.95m));
            Assert.That(result.DraftRecall, Is.EqualTo(.97m));
            Assert.That(result.Delta!.Recall, Is.EqualTo(.02m));
            Assert.That(result.ActiveReduction, Is.EqualTo(.91m));
            Assert.That(result.DraftReduction, Is.EqualTo(.94m));
            Assert.That(result.Delta.Reduction, Is.EqualTo(.03m));
            Assert.That(result.Explanation, Does.Contain("não constitui dossiê FS"));
        }));
    }

    [Test]
    public void View_WhenActiveChanges_MarksEvidenceStaleWithoutRecyclingPreviousDeltas()
    {
        var result = ModelGovernanceReadOnlyService.EvaluateEvidence(
            Measurements(Guid.NewGuid().ToString("N")), Active());
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.Status, Is.EqualTo("EVIDENCIA_OBSOLETA"));
            Assert.That(result.ActiveRecall, Is.Null);
            Assert.That(result.DraftRecall, Is.Null);
            Assert.That(result.Delta, Is.Null);
        }));
    }

    [Test]
    public void View_MissingActiveOrIncompleteMetrics_NeverInventsBaselineOrZeroDifference()
    {
        var missingActive = ModelGovernanceReadOnlyService.EvaluateEvidence(
            Measurements("SEM_ATIVO", 2m, 0m), null);
        var incomplete = Measurements(ActiveId.ToString("N"));
        incomplete.Remove("DT15_BLOCKING_ACTIVE_RECALL");
        var incompleteResult = ModelGovernanceReadOnlyService.EvaluateEvidence(incomplete, Active());
        var missingDenominator = Measurements(ActiveId.ToString("N"));
        missingDenominator.Remove("DT15_BLOCKING_PAIR_M_WEIGHT");
        var missingDenominatorResult = ModelGovernanceReadOnlyService.EvaluateEvidence(
            missingDenominator, Active());
        Assert.Multiple((Action)(() =>
        {
            Assert.That(missingActive.Status, Is.EqualTo("NAO_COMPARAVEL"));
            Assert.That(missingActive.DraftRecall, Is.EqualTo(.97m));
            Assert.That(missingActive.ActiveRecall, Is.Null);
            Assert.That(missingActive.Delta, Is.Null);
            Assert.That(incompleteResult.Status, Is.EqualTo("INCOMPLETO"));
            Assert.That(incompleteResult.Delta, Is.Null);
            Assert.That(missingDenominatorResult.Status, Is.EqualTo("INCOMPLETO"));
            Assert.That(missingDenominatorResult.Delta, Is.Null);
            Assert.That(ModelGovernanceReadOnlyService.EvaluateEvidence(
                new Dictionary<string, (decimal, string)>(), Active()).Status,
                Is.EqualTo("AGUARDA_EVIDENCIA"));
        }));
    }

    [Test]
    public void View_TamperedDeltaOrMixedMethodIsNotComparable()
    {
        var tampered = Measurements(ActiveId.ToString("N"));
        tampered["DT15_BLOCKING_RECALL_DELTA"] =
            (0.99m, ModelGovernanceReadOnlyService.PartialMethod + ":" + ActiveId.ToString("N"));
        var mixed = Measurements(ActiveId.ToString("N"));
        mixed["DT15_BLOCKING_DRAFT_RECALL"] = (.97m, "UNVERIFIED_METHOD");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(ModelGovernanceReadOnlyService.EvaluateEvidence(
                tampered, Active()).Status, Is.EqualTo("NAO_COMPARAVEL"));
            Assert.That(ModelGovernanceReadOnlyService.EvaluateEvidence(
                mixed, Active()).Status, Is.EqualTo("NAO_COMPARAVEL"));
        }));
    }

    [Test]
    public void MasterPermissionAndSyntheticCredential_AreSeparateFromMonitor()
    {
        var root = Root();
        var security = File.ReadAllText(Path.Combine(root,
            "Solution", "src", "Jornada.Access.Security", "JornadaAccessSecurity.cs"));
        var program = File.ReadAllText(Path.Combine(root,
            "Solution", "src", "Jornada.Api", "Program.cs"));
        var api = File.ReadAllText(Path.Combine(root,
            "Solution", "src", "Jornada.Api", "ModelGovernanceReadOnlyApi.cs"));
        var html = File.ReadAllText(Path.Combine(root,
            "Solution", "src", "Jornada.Api", "wwwroot", "governanca", "modelos", "index.html"));
        using var keys = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "Solution", "config", "security", "test-access-keys.json")));
        var master = keys.RootElement.GetProperty("credentials").EnumerateArray()
            .Single(x => x.GetProperty("publicCode").GetString() == "MASTER_DEV");
        var scopes = master.GetProperty("scopes").EnumerateArray()
            .Select(x => x.GetString()).ToArray();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(security, Does.Contain("(\"jornada.modelos.governanca.read\", false)"));
            Assert.That(scopes, Is.EquivalentTo(new[] { "jornada.modelos.governanca.read" }));
            Assert.That(scopes, Does.Not.Contain("jornada.monitor.read"));
            Assert.That(program, Does.Contain("app.MapModelGovernanceReadOnlyApi();"));
            Assert.That(program, Does.Not.Contain("app.MapOperationalMonitorApi();"),
                "O Monitor é registrado pela API de origem; não duplicar rotas.");
            Assert.That(api, Does.Contain("if (!environment.IsDevelopment())"));
            Assert.That(api, Does.Contain("context.PublicCode, \"MASTER_DEV\""));
            Assert.That(api, Does.Contain(".RequireAuthorization(Permission)"));
            Assert.That(api, Does.Not.Contain("app.MapPost("));
            Assert.That(api, Does.Not.Contain("app.MapPut("));
            Assert.That(api, Does.Not.Contain("app.MapDelete("));
            Assert.That(File.ReadAllText(Path.Combine(root, "Solution", "src", "Jornada.Api",
                "ModelGovernanceReadOnlyService.cs")), Does.Contain("modelo_linkage_dossie_decisao"));
            Assert.That(File.ReadAllText(Path.Combine(root, "Solution", "src", "Jornada.Api",
                "ModelGovernanceReadOnlyService.cs")), Does.Contain("DecisionDossiers"));
            Assert.That(html, Does.Contain("Governança de modelos"));
            Assert.That(html, Does.Contain("Nenhuma consulta altera o modelo ATIVO"));
            Assert.That(html, Does.Contain("Credenciais não são gravadas no navegador"));
            Assert.That(html, Does.Not.Contain("localStorage.setItem"));
            Assert.That(html, Does.Not.Contain("sessionStorage.setItem"));
        }));
    }

    private static GovernanceModel Active() =>
        new(ActiveId, 3, "ATIVO", null, null, "MODEL_3_BLOCKING_V1",
            new string('a', 64), new string('b', 64), []);

    private static Dictionary<string, (decimal Value, string Method)> Measurements(
        string source, decimal status = 1m, decimal comparable = 1m)
    {
        var method = ModelGovernanceReadOnlyService.PartialMethod + ":" + source;
        return new Dictionary<string, (decimal, string)>(StringComparer.Ordinal)
        {
            ["DT15_BLOCKING_PAIR_STATUS_CODE"] = (status, method),
            ["DT15_BLOCKING_PAIR_COMPARABLE"] = (comparable, method),
            ["DT15_BLOCKING_PAIR_M_WEIGHT"] = (30m, method),
            ["DT15_BLOCKING_PAIR_U_WEIGHT"] = (70m, method),
            ["DT15_BLOCKING_BASE_VERSION"] = (3m, method),
            ["DT15_BLOCKING_DRAFT_RECALL"] = (.97m, method),
            ["DT15_BLOCKING_DRAFT_REDUCTION"] = (.94m, method),
            ["DT15_BLOCKING_ACTIVE_RECALL"] = (.95m, method),
            ["DT15_BLOCKING_ACTIVE_REDUCTION"] = (.91m, method),
            ["DT15_BLOCKING_RECALL_DELTA"] = (.02m, method),
            ["DT15_BLOCKING_REDUCTION_DELTA"] = (.03m, method)
        };
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(dir.FullName, "Solution")))
                return dir.FullName;
            dir = dir.Parent;
        }
        Assert.Fail("Raiz do repositório Jornada ausente.");
        return string.Empty;
    }
}
