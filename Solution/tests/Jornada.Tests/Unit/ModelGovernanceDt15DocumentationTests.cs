using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ModelGovernanceDt15DocumentationTests
{
    [Test]
    public void Dt15_PrioritizesPairedCurrentVersusDraftOnRestrictedMasterPage()
    {
        var root = FindRepositoryRoot();
        var docs = Path.Combine(root, "Solution", "docs");
        var decisionPath = Path.Combine(docs, "DT15_Governanca_Decisao_Modelo.md");
        Assert.That(File.Exists(decisionPath), Is.True, "DT-15 deve ter contrato específico.");
        var decision = File.ReadAllText(decisionPath);
        var backlog = File.ReadAllText(Path.Combine(docs, "Dividas_Tecnicas.md"));
        var plan = File.ReadAllText(Path.Combine(docs, "Plano_Desenvolvimento.md"));
        var monitor = File.ReadAllText(Path.Combine(docs, "Monitor_Operacional.md"));
        var runbook = File.ReadAllText(Path.Combine(docs, "Runbook_Operacao.md"));
        var readme = File.ReadAllText(Path.Combine(docs, "README.md"));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(decision, Does.Contain("atual × proposto"));
            Assert.That(decision, Does.Contain("página restrita de governança"));
            Assert.That(decision, Does.Contain("histórico agregado"));
            Assert.That(decision, Does.Contain("mesmo corpus"));
            Assert.That(decision, Does.Contain("replay contrafactual"));
            Assert.That(decision, Does.Contain("SEM_COMPARATIVO_PAREADO"));
            Assert.That(decision, Does.Contain("NAO_COMPARAVEL"));
            Assert.That(decision, Does.Contain("Monitor Operacional"));
            Assert.That(decision, Does.Contain("jornada.monitor.read"));
            Assert.That(decision, Does.Contain("aprovação humana"));
            Assert.That(decision, Does.Contain("VALIDATE"));
            Assert.That(decision, Does.Contain("ACTIVATE"));
            Assert.That(decision, Does.Contain("Invoke-JornadaLinkageCalibration.ps1"));
            Assert.That(decision, Does.Contain("ainda não implementados"));
            Assert.That(backlog, Does.Contain("| DT-15 |"));
            Assert.That(backlog, Does.Contain("DT15_Governanca_Decisao_Modelo.md"));
            Assert.That(plan, Does.Contain("Governança humana [DT-15]"));
            Assert.That(monitor, Does.Contain("Fronteira com a [DT-15]"));
            Assert.That(monitor, Does.Contain("somente"));
            Assert.That(runbook, Does.Contain("prévia master independente do Monitor e somente leitura"));
            Assert.That(readme, Does.Contain("DT15_Governanca_Decisao_Modelo.md"));
        }));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(current.FullName, "Solution"))
                && Directory.Exists(Path.Combine(current.FullName, "Documentos")))
                return current.FullName;

            current = current.Parent;
        }

        Assert.Fail("Raiz do repositório Jornada não encontrada.");
        return string.Empty;
    }
}
