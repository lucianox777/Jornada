using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class ProductScopeDocumentationTests
{
    [Test]
    public void Phase1Scope_DoesNotConflateConcessionWithPaymentOrOverstateCoverage()
    {
        var root = FindRepositoryRoot();
        var scopePath = Path.Combine(root, "Solution", "docs", "Escopo_Produto_Fase1.md");
        var docsReadmePath = Path.Combine(root, "Solution", "docs", "README.md");
        var semanticModelPath = Path.Combine(
            root,
            "Solution",
            "bi",
            "Jornada.SemanticModel",
            "definition",
            "tables",
            "BeneficiosConcedidos.tmdl");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(scopePath), Is.True, "Documento canônico de limites da Fase 1 ausente.");
            Assert.That(File.Exists(docsReadmePath), Is.True);
            Assert.That(File.Exists(semanticModelPath), Is.True);
        });

        var scope = File.ReadAllText(scopePath);
        var docsReadme = File.ReadAllText(docsReadmePath);
        var semanticModel = File.ReadAllText(semanticModelPath);

        Assert.Multiple(() =>
        {
            Assert.That(docsReadme, Does.Contain("Escopo_Produto_Fase1.md"));
            Assert.That(scope, Does.Contain("Benefício Concedido não é sinônimo de pagamento"));
            Assert.That(scope, Does.Contain("`PAGAMENTO` e `RECEBIMENTO`").And.Contain("fora do runtime factual da Fase 1"));
            Assert.That(scope, Does.Contain("Gestores e Tipos/versões efetivamente integrados e publicados"));
            Assert.That(scope, Does.Contain("Ausência de registro fora dessa cobertura não deve ser convertida em afirmação de inexistência administrativa no Município"));
            Assert.That(scope, Does.Contain("não de identidade"));
            Assert.That(semanticModel, Does.Contain("Não representa pagamento ocorrido, despesa orçamentária ou desembolso"));
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
