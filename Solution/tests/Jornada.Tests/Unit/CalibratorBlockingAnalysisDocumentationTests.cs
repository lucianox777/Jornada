using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class CalibratorBlockingAnalysisDocumentationTests
{
    [Test]
    public void BlockingAnalysis_RemainsAnalyticalFailClosedAndDoesNotInventSurnameSemantics()
    {
        var root = FindRepositoryRoot();
        var planPath = Path.Combine(root, "Solution", "docs", "Calibrador_Plano_Blocking_Analise.md");
        var readmePath = Path.Combine(root, "Solution", "docs", "README.md");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(planPath), Is.True, "Plano analítico do Calibrador ausente.");
            Assert.That(File.Exists(readmePath), Is.True, "Índice de documentação técnica ausente.");
        });

        var plan = File.ReadAllText(planPath);
        var readme = File.ReadAllText(readmePath);

        Assert.Multiple(() =>
        {
            Assert.That(readme, Does.Contain("Calibrador_Plano_Blocking_Analise.md"));
            Assert.That(plan, Does.Contain("análise técnica e proposições"));
            Assert.That(plan, Does.Contain("não homologa política de blocking"));
            Assert.That(plan, Does.Contain("CPF válido/confiável permanece fora deste plano"));
            Assert.That(plan, Does.Contain("TrueMatchRecall"));
            Assert.That(plan, Does.Contain("ReductionRatio"));
            Assert.That(plan, Does.Contain("contribuição marginal"));
            Assert.That(plan, Does.Contain("P22 — sobrenome não materializado"));
            Assert.That(plan, Does.Contain("limitação semântica deliberada / gate de fonte estruturada"));
            Assert.That(plan, Does.Contain("não materializar sobrenome por inferência a partir de `nome_completo`"));
            Assert.That(plan, Does.Contain("não podem receber frequência oficial de `Surname` do IBGE"));
            Assert.That(plan, Does.Contain("não propõe valores numéricos"));
            Assert.That(plan, Does.Contain("qualquer promoção continua fail-closed"));
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
