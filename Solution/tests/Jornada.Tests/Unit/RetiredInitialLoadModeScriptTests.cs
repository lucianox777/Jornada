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

        // O E2E sintético pode montar temporariamente as fixtures externas SEHAB,
        // sem manter uma Solução de Apoio dentro do monorepo.
        var localE2e = File.ReadAllText(Path.Combine(
            root, "Solution", "scripts", "local-e2e.ps1"));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(localE2e, Does.Contain("tests/fixtures/external-contracts/gestores/SEHAB"));
            Assert.That(localE2e, Does.Contain("Get-FileHash"));
            Assert.That(localE2e, Does.Contain("Remove-Item -LiteralPath $stagedSehab"));
            Assert.That(localE2e, Does.Contain("cópia runtime SEHAB diverge da fixture externa"));
        }));

        var localE2eBash = File.ReadAllText(Path.Combine(
            root, "Solution", "scripts", "local-e2e.sh"));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(localE2eBash, Does.Contain("tests/fixtures/external-contracts/gestores/SEHAB"));
            Assert.That(localE2eBash, Does.Not.Contain("ApoioSecretarias"));
            Assert.That(localE2eBash, Does.Not.Contain("SolucaoApoioSecretarias.sln"));
        }));

        foreach (var name in new[]
        {
            "local-e2e.ps1",
            "dt05-cpf-late-wave.ps1",
            "dt05-runner-e2e.ps1"
        })
        {
            var source = File.ReadAllText(Path.Combine(root, "Solution", "scripts", name));
            Assert.Multiple((Action)(() =>
            {
                Assert.That(source, Does.Not.Contain(
                    "SELECT ativo FROM controle.modo_carga_inicial"),
                    $"{name}: tabela foi intencionalmente removida.");
                Assert.That(source, Does.Contain("identidade.modelo_linkage"),
                    $"{name}: não remover preflight de modelo ATIVO.");
            }));
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
