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
            Assert.That(html, Does.Contain("Conferência de implementação"));
            Assert.That(html, Does.Contain("Round-trip do formato"));
            Assert.That(html, Does.Contain("JORNADA_CALIBRATION_AUDIT_ROUNDTRIP_V1"));
            Assert.That(html, Does.Contain("OBRIGATORIO_NO_EXPORT_NAO_PERSISTIDO"));
            Assert.That(html, Does.Contain("Snapshot do modelo"));
            Assert.That(html, Does.Contain("OBSOLETA"));
            Assert.That(html, Does.Contain("Conferência de implementação, round-trip de formato e validação estatística são evidências distintas."));
            Assert.That(html, Does.Not.Contain("T_LINKAGE"));
            Assert.That(service, Does.Contain("auditoria.modelo_linkage_estado_evento"));
            Assert.That(service, Does.Contain("auditoria.linkage_conferencia_evidencia"));
            Assert.That(service, Does.Contain("sp_calcular_fingerprint_modelo_linkage"));
            Assert.That(service, Does.Contain("snapshot_current"));
            Assert.That(service, Does.Contain("SEM_EVIDENCIA_MODELO_ATIVO"));
            Assert.That(service, Does.Contain("JORNADA_CALIBRATION_AUDIT_ROUNDTRIP_V1"));
            Assert.That(service, Does.Contain("NOMINAL_U_NOME_SOURCE_BLOCKING_CONDITIONED"));
            Assert.That(service, Does.Contain("frequencia_nome_versao_id"));
            Assert.That(service, Does.Contain("SELECT TOP(5) linkage_run_id,modelo_id,tipo_run,status,modelo_versao"));
            Assert.That(html, Does.Contain("shortId(x.modelId)"));
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
