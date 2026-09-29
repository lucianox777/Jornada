using NUnit.Framework;

namespace Jornada.Tests.Unit;

/// <summary>
/// DT-17: regressão documental da fronteira IBGE/score.
/// Testes de texto verificam o contrato escrito; não comprovam execução do scorer,
/// integridade da referência, suficiência estatística ou ausência de caminhos indiretos.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class Dt17IbgeBootstrapOnlyDocumentationTests
{
    private const string CentralDecision =
        "Solution/docs/Decisoes_Linkage_Calibracao_IBGE_20260926.md";
    private const string MunicipalDecision =
        "Solution/docs/Linkage_Bootstrap_U_Municipio_SP_20260926.md";

    [Test]
    public void BothDecisions_KeepIbgeOnlyInBootstrapAndNotInOperationalScore()
    {
        var central = Read(CentralDecision);
        var municipal = Read(MunicipalDecision);
        var centralScope = Between(central,
            "## 4. Referência IBGE e derivados u (pré-primeira carga)",
            "## 5. Monitor e execução");

        Assert.Multiple(() =>
        {
            Assert.That(centralScope, Does.Contain(
                "O IBGE é usado exclusivamente no bootstrap do `u` por nível de concordância"));
            Assert.That(centralScope, Does.Contain(
                "sem consulta ao IBGE por nome no score operacional"));
            Assert.That(municipal, Does.Contain(
                "o resultado do Monte Carlo é `u` agregado por nível de concordância"),
                "A referência deve produzir u agregado por estado, não por nome.");
            Assert.That(municipal, Does.Contain(
                "O scorer operacional utiliza exclusivamente os parâmetros `U_*` persistidos"));
            Assert.That(municipal, Does.Contain("Não consultar nomes do IBGE no score"));
            Assert.That(centralScope, Does.Contain("sem term frequency"));
            Assert.That(municipal, Does.Contain("não introduz term frequency"));
        });
    }

    [Test]
    public void BothDecisions_AllowGovernedNationalFallbackOnlyForInsufficientMunicipalMarginal()
    {
        var central = Between(Read(CentralDecision),
            "## 4. Referência IBGE e derivados u (pré-primeira carga)",
            "## 5. Monitor e execução");
        var municipal = Read(MunicipalDecision);

        foreach (var (name, document) in new[]
        {
            ("Decisão central", central),
            ("Decisão municipal", municipal)
        })
        {
            Assert.Multiple(() =>
            {
                Assert.That(document, Does.Contain("MARGINAL_INSUFICIENTE"), name);
                Assert.That(document, Does.Contain(name == "Decisão central" ? "não entra nos sorteios" : "não participa do sorteio"), name);
                Assert.That(document, Does.Contain("cobertura"), name);
                Assert.That(document, Does.Contain("u=0"), name);
                Assert.That(document, Does.Contain("pseudo-contagem"), name);
                Assert.That(document, Does.Contain("UF"), name);
                Assert.That(document, Does.Contain("Brasil"), name);
                Assert.That(document, Does.Not.Contain("LLR=0"),
                    name + ": nome ausente do IBGE nunca neutraliza LLR.");
                Assert.That(document, Does.Not.Contain("LLR neutro só nesse componente"), name);
            });
        }
        Assert.Multiple(() =>
        {
            Assert.That(central, Does.Contain("não ativar V2"));
            Assert.That(municipal, Does.Contain("V2 não ativa"));
            Assert.That(central, Does.Contain("fallback nacional V1 integral validado"),
                "A marginal municipal insuficiente pode selecionar V1 nacional íntegra.");
            Assert.That(municipal, Does.Contain("V1 nacional integral da pessoa"));
            Assert.That(central, Does.Contain("nunca V2 municipal com complemento oculto"),
                "O resultado nacional deve ser identificado como V1, sem mistura de marginais.");
            Assert.That(municipal, Does.Contain("nunca V2 com mistura municipal/nacional"));
            Assert.That(central, Does.Contain(
                "Se a referência nacional também for insuficiente, `GENERATE_DRAFT` falha explicitamente"));
            Assert.That(municipal, Does.Contain(
                "Se a V1 nacional também for insuficiente, `GENERATE_DRAFT` falha explicitamente"));
            Assert.That(central, Does.Contain("Não existe fallback por ausência de nome individual"));
            Assert.That(municipal, Does.Contain(
                "nem recorrer a UF ou Brasil **para completar nome individual**"));
        });
    }

    [Test]
    public void BothDecisions_RequireCorpusCoverageAndExplainRarityVersusSpelling()
    {
        var central = Read(CentralDecision);
        var municipal = Read(MunicipalDecision);

        foreach (var document in new[] { central, municipal })
        {
            Assert.Multiple(() =>
            {
                Assert.That(document, Does.Contain("raridade/supressão"));
                Assert.That(document, Does.Contain("grafia"));
                Assert.That(document, Does.Contain("indeterminada"));
                Assert.That(document, Does.Contain("100% do corpus"));
                Assert.That(document, Does.Contain("Ensaio"));
            });
        }
    }

    [Test]
    public void ExternalSplinkSection_RemainsExplicitlyOutsideThisContract()
    {
        var central = Read(CentralDecision);
        Assert.That(central, Does.Contain(
            "## 2.1 Conferência externa Jornada × Splink — decisão consolidada de 26/09/2026"));
        Assert.That(central, Does.Contain(
            "## 3. Corpus: diversidade não é demonstrada apenas por N"));
    }

    private static string Read(string relativePath)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(
            directory.FullName, "RELEASE_INFO.txt")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, "Raiz do repositório não encontrada.");
        var path = Path.Combine(directory!.FullName,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.That(File.Exists(path), Is.True, "Documento ausente: " + relativePath);
        return File.ReadAllText(path);
    }

    private static string Between(string content, string start, string end)
    {
        var first = content.IndexOf(start, StringComparison.Ordinal);
        var last = content.IndexOf(end, StringComparison.Ordinal);
        Assert.That(first, Is.GreaterThanOrEqualTo(0), start);
        Assert.That(last, Is.GreaterThan(first), end);
        return content[first..last];
    }
}
