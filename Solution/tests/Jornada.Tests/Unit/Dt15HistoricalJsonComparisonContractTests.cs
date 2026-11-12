using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class Dt15HistoricalJsonComparisonContractTests
{
    [Test]
    public void Governance_history_uses_original_metrics_and_does_not_mislabel_reference_hash()
    {
        var root = FindRoot();
        var api = File.ReadAllText(Path.Combine(root, "Solution", "src", "Jornada.Api", "ModelGovernanceReadOnlyService.cs"));
        var html = File.ReadAllText(Path.Combine(root, "Solution", "src", "Jornada.Api", "wwwroot", "governanca", "modelos", "index.html"));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(api, Does.Contain("ReadHistoricalMetricsAsync"));
            Assert.That(api, Does.Contain("HistoricalMetrics = historicalMetrics"));
            Assert.That(api, Does.Contain("FS_DECISION_CALIBRATION_VALIDATION_FP"));
            Assert.That(api, Does.Contain("FS_DECISION_CALIBRATION_TEST_FN"));
            Assert.That(html, Does.Contain("showHistoricalComparison(data.active, data.draft, data.calibrationHistory, data.historicalMetrics)"));
            Assert.That(html, Does.Contain("não é SHA do JSON"));
            Assert.That(html, Does.Contain("Nome do JSON, SHA-256 dos bytes originais"));
            Assert.That(html, Does.Contain("Recomendação de ativação: indisponível"));
        }));
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RELEASE_INFO.txt")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Solution"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Raiz do Jornada não encontrada.");
    }
}
