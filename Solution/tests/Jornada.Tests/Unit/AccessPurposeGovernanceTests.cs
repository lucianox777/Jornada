using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class AccessPurposeGovernanceTests
{
    [Test]
    public void PendingInstitutionalDecision_DoesNotBecomeFreeRequestPurposeParameter()
    {
        var root = FindRepositoryRoot();
        var governancePath = Path.Combine(root, "Solution", "docs", "Governanca_Finalidade_Acesso.md");
        var apiDocsPath = Path.Combine(root, "Solution", "docs", "API.md");
        var securityReadmePath = Path.Combine(root, "Solution", "config", "security", "README.md");
        var openApiPath = Path.Combine(root, "Solution", "openapi", "jornada-v1.openapi.json");
        var programPath = Path.Combine(root, "Solution", "src", "Jornada.Api", "Program.cs");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(governancePath), Is.True, "Gate documental de finalidade de acesso ausente.");
            Assert.That(File.Exists(apiDocsPath), Is.True);
            Assert.That(File.Exists(securityReadmePath), Is.True);
            Assert.That(File.Exists(openApiPath), Is.True);
            Assert.That(File.Exists(programPath), Is.True);
        });

        var governance = File.ReadAllText(governancePath);
        var apiDocs = File.ReadAllText(apiDocsPath);
        var securityReadme = File.ReadAllText(securityReadmePath);
        var openApi = File.ReadAllText(openApiPath);
        var program = File.ReadAllText(programPath);

        Assert.Multiple(() =>
        {
            Assert.That(governance, Does.Contain("gate de decisão institucional"));
            Assert.That(governance, Does.Contain("não fecha a decisão do CCGD"));
            Assert.That(governance, Does.Contain("credencial/contrato de projeção autorizado"));
            Assert.That(governance, Does.Contain("auditoria persistente"));
            Assert.That(apiDocs, Does.Contain("Consultas de Pessoa e Identidade não exigem `X-Jornada-Finalidade`"));
            Assert.That(securityReadme, Does.Contain("não carregam catálogo ou allowlist de finalidade"));
            Assert.That(openApi, Does.Not.Contain("X-Jornada-Finalidade"));
            Assert.That(program, Does.Not.Contain("X-Jornada-Finalidade"));
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt")) &&
                Directory.Exists(Path.Combine(current.FullName, "Solution")) &&
                Directory.Exists(Path.Combine(current.FullName, "Documentos")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        Assert.Fail("Raiz do repositório Jornada não encontrada a partir do diretório de testes.");
        return string.Empty;
    }
}