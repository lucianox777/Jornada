using System.Text.RegularExpressions;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SecretScanningConfigurationTests
{
    [Test]
    public void Gitleaks_config_extends_defaults_without_broad_global_allowlist()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, ".gitleaks.toml");
        Assert.That(File.Exists(path), Is.True, "Configuração Gitleaks deve existir na raiz do repositório.");

        var config = File.ReadAllText(path);
        Assert.Multiple(() =>
        {
            Assert.That(config, Does.Contain("[extend]"));
            Assert.That(config, Does.Match(new Regex(@"(?m)^useDefault\s*=\s*true\s*$")));
            Assert.That(config, Does.Not.Contain("[[allowlists]]"),
                "A configuração inicial não deve silenciar achados por allowlist global.");
            Assert.That(config, Does.Not.Contain("[allowlist]"));
        });
    }

    [Test]
    public void Local_secret_scan_reports_are_covered_by_gitignore()
    {
        var root = FindRepositoryRoot();
        var ignore = File.ReadAllText(Path.Combine(root, "Solution", ".gitignore"));
        Assert.That(ignore, Does.Match(new Regex(@"(?m)^\.local/\s*$")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Documentos"))
                && Directory.Exists(Path.Combine(directory.FullName, "Solution")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Raiz do repositório Jornada não encontrada.");
    }
}
