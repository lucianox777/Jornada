using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class RetiredInitialLoadModeScriptTests
{
    [Test]
    public void RunnerE2eScripts_DoNotQueryTheTableRemovedBySeptemberMigration()
    {
        var root = FindRoot();
        var migration = File.ReadAllText(Path.Combine(
            root, "Solution", "database", "migrations",
            "20260927_Remove_Modo_Carga_Inicial.sql"));
        Assert.That(migration, Does.Contain(
            "DROP TABLE IF EXISTS controle.modo_carga_inicial"));

        foreach (var name in new[]
        {
            "local-e2e.ps1",
            "dt05-cpf-late-wave.ps1",
            "dt05-runner-e2e.ps1"
        })
        {
            var source = File.ReadAllText(Path.Combine(root, "Solution", "scripts", name));
            Assert.Multiple(() =>
            {
                Assert.That(source, Does.Not.Contain(
                    "SELECT ativo FROM controle.modo_carga_inicial"),
                    $"{name}: tabela foi intencionalmente removida.");
                Assert.That(source, Does.Contain("identidade.modelo_linkage"),
                    $"{name}: não remover preflight de modelo ATIVO.");
            });
        }
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(dir.FullName, "Solution")))
                return dir.FullName;
            dir = dir.Parent;
        }

        Assert.Fail("Raiz do repositório Jornada não encontrada.");
        return string.Empty;
    }
}
