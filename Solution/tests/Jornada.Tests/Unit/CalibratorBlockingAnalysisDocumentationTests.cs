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
        var decisionPath = Path.Combine(root, "Solution", "docs",
            "Decisao_Arquitetural_Blocking_Complementar_IBGE_20260927.md");

        Assert.Multiple((Action)(() =>
        {
            Assert.That(File.Exists(planPath), Is.True, "Plano analítico do Calibrador ausente.");
            Assert.That(File.Exists(readmePath), Is.True, "Índice de documentação técnica ausente.");
            Assert.That(File.Exists(decisionPath), Is.True, "Decisão canônica de blocking complementar ausente.");
        }));

        var plan = File.ReadAllText(planPath);
        var readme = File.ReadAllText(readmePath);
        var decision = File.ReadAllText(decisionPath);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(readme, Does.Contain("Calibrador_Plano_Blocking_Analise.md"));
            Assert.That(plan, Does.Contain("análise técnica e proposições"));
            Assert.That(plan, Does.Contain("não homologa política de blocking"));
            Assert.That(plan, Does.Contain("CPF válido/confiável permanece fora deste plano"));
            Assert.That(plan, Does.Contain("TrueMatchRecall"));
            Assert.That(plan, Does.Contain("ReductionRatio"));
            Assert.That(plan, Does.Contain("contribuição marginal"));
            Assert.That(plan, Does.Contain("P22 — fronteira estruturada e índice por presença"));
            Assert.That(plan, Does.Contain("limitação semântica deliberada / gate de fonte estruturada"));
            Assert.That(plan, Does.Contain("não materializar **sobrenome semanticamente estruturado**"));
            Assert.That(plan, Does.Contain("referência marginal útil"));
            Assert.That(plan, Does.Contain("não podem receber diretamente"));
            Assert.That(plan, Does.Contain("**complementares**"));
            Assert.That(decision, Does.Contain("5.2. Planejador estatístico multivariado"));
            Assert.That(decision, Does.Contain("Distribuição demográfica da população"));
            Assert.That(decision, Does.Contain("Distribuição condicional de erros"));
            Assert.That(decision, Does.Contain("interseção de índices simples EAV"));
            Assert.That(decision, Does.Contain("`SOUSA` **não** substitui automaticamente `SOUZA`"));
            Assert.That(decision, Does.Contain("5.3. Contrato de seleção de passes"));
            Assert.That(decision, Does.Contain("2.0. Evolução paralela, observação comparativa e eventual desativação"));
            Assert.That(decision, Does.Contain("D∪C"));
            Assert.That(decision, Does.Contain("Retirada facultativa mediante evidência"));
            Assert.That(plan, Does.Contain("10.0. Benchmark paralelo e critérios objetivos de manutenção"));
            Assert.That(plan, Does.Contain("desenvolver em paralelo"));
            Assert.That(plan, Does.Contain("Possibilidade de abandono"));
            Assert.That(decision, Does.Contain("não pode retornar `NOVA_IDENTIDADE`"));
            Assert.That(plan, Does.Contain("10.3. Critério de aceite do índice estatístico multivariado"));
            Assert.That(plan, Does.Contain("fonética, ortografia e aliases"));
            Assert.That(decision, Does.Contain("29 de fevereiro é registrável em cartório"));
            Assert.That(decision, Does.Contain("Janela etária da população viva"));
            Assert.That(decision, Does.Contain("data_civil_de_referencia_do_run"));
            Assert.That(decision, Does.Contain("históricos e pessoas falecidas"));
            Assert.That(plan, Does.Contain("não propõe valores numéricos"));
            Assert.That(plan, Does.Contain("qualquer promoção continua fail-closed"));
        }));
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
