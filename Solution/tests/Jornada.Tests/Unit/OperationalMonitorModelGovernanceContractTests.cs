using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class OperationalMonitorModelGovernanceContractTests
{
    [Test]
    public void Monitor_exposes_read_only_model_governance_without_sensitive_thresholds()
    {
        var root = FindRepositoryRoot();
        var html = File.ReadAllText(Path.Combine(root, "Solution", "src", "Jornada.Api", "wwwroot", "monitor", "index.html"));
        var service = File.ReadAllText(Path.Combine(root, "Solution", "src", "Jornada.Api", "OperationalMonitor.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("Linkage · governança do modelo"));
            Assert.That(html, Does.Contain("Validação estatística"));
            Assert.That(html, Does.Contain("PENDENTE_ISSUE_31"));
            Assert.That(html, Does.Contain("Threshold/margem não são expostos"));
            Assert.That(html, Does.Not.Contain("T_LINKAGE"));
            Assert.That(service, Does.Contain("auditoria.modelo_linkage_estado_evento"));
            Assert.That(service, Does.Contain("NOMINAL_U_NOME_SOURCE_BLOCKING_CONDITIONED"));
            Assert.That(service, Does.Contain("frequencia_nome_versao_id"));
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(current.FullName, "Solution")))
                return current.FullName;
            current = current.Parent;
        }

        Assert.Fail("Raiz do repositório Jornada não encontrada.");
        return string.Empty;
    }
}
