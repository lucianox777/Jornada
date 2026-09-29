using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class LocalClusterDiagnosisContractTests
{
    [TestCase("scripts/local-cluster.ps1")]
    [TestCase("scripts/local-cluster.sh")]
    public void LinkageDiagnosis_TargetsPublishedRunFromActiveCalibratedModel(string relativePath)
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "Solution", relativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("status='ATIVO'"));
            Assert.That(script, Does.Contain("SEED_DEV_FIXO_NAO_TREINADO"));
            Assert.That(script, Does.Contain("AND modelo_id="));
            Assert.That(script, Does.Contain("Execute primeiro"));
            Assert.That(script, Does.Contain("linkage"));
        });
    }

    [TestCase("scripts/local-cluster.ps1")]
    [TestCase("scripts/local-cluster.sh")]
    public void CalibrationMessage_DescribesIbgeReferenceAsBootstrapData(string relativePath)
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "Solution", relativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("IBGE"));
            Assert.That(script, Does.Contain("bootstrap nominal obrigatório"));
            Assert.That(script, Does.Contain("fonte substituta"));
            Assert.That(script, Does.Not.Contain("bootstrap/fallback"));
            Assert.That(script, Does.Contain("u nominal"));
            Assert.That(script, Does.Contain("blocking"));
            Assert.That(script, Does.Not.Contain("Se a referência IBGE ainda não estiver materializada"));
        });
    }

    [TestCase("Documentos/ADR/ADR-002-calibrador-fs-u-condicionado.md")]
    [TestCase("Solution/docs/Calibrador_FS_Specification.md")]
    public void CurrentIbgeNorms_RequireSingleBootstrapWithoutPerGenerationActiveReference(string relativePath)
    {
        var root = FindRepositoryRoot();
        var document = File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Multiple(() =>
        {
            Assert.That(document, Does.Contain("IBGE"));
            Assert.That(document, Does.Contain("uma única vez"));
            Assert.That(document, Does.Contain("GENERATE_DRAFT"));
            Assert.That(document, Does.Match(@"(?i)(sem exigir|não exige) referência IBGE ativa"));
            Assert.That(document, Does.Match(@"(?i)(falha por ausência do primeiro bootstrap|primeiro bootstrap necessário ainda não ocorreu)"));
            Assert.That(document, Does.Not.Contain("bootstrap/fallback"));
            Assert.That(document, Does.Not.Contain("fallback versionado"));
        });
    }

    // Estes dois documentos ainda trazem a regra histórica de snapshot ativo a cada
    // geração. São inventariados, mas NÃO são aceitos por este teste como norma vigente.
    // A atualização dos respectivos contratos pertence às frentes documentais próprias.
    [TestCase("Solution/docs/Arquitetura_Identidade_Linkage.md")]
    [TestCase("Documentos/Requisitos/02_Requisitos_Funcionais_Jornada_v1.1.md")]
    public void PendingIbgeDocumentationReconciliation_RemainsInventoried(string relativePath)
    {
        var root = FindRepositoryRoot();
        var document = File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Multiple(() =>
        {
            Assert.That(document, Does.Contain("IBGE"));
            Assert.That(document, Does.Contain("bootstrap"));
            Assert.That(document, Does.Contain("GENERATE_DRAFT"));
        });
    }

    [Test]
    public void HistoricalRf052_PreservesOriginalWordingButIdentifiesSupersedingRule()
    {
        var root = FindRepositoryRoot();
        var historical = File.ReadAllText(Path.Combine(root, "Documentos", "Requisitos",
            "Historico", "02_Requisitos_Funcionais_Jornada_Aditivo_v1.1.md"));

        Assert.Multiple(() =>
        {
            Assert.That(historical, Does.Contain("redação original de 09/09/2026"));
            Assert.That(historical, Does.Contain("superada em 28/09/2026"));
            Assert.That(historical, Does.Contain("Nota de atualização normativa"));
            Assert.That(historical, Does.Contain("GENERATE_DRAFT deve falhar explicitamente"));
            Assert.That(historical, Does.Contain("fonte substituta"));
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
