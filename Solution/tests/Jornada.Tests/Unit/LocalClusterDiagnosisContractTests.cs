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
            Assert.That(script, Does.Contain("bootstrap/fallback"));
            Assert.That(script, Does.Contain("u nominal"));
            Assert.That(script, Does.Contain("blocking"));
            Assert.That(script, Does.Not.Contain("Se a referência IBGE ainda não estiver materializada"));
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
