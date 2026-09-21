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
                "A configuração não deve silenciar achados por allowlist global.");
            Assert.That(config, Does.Not.Contain("[allowlist]"));
            Assert.That(config, Does.Contain("id = \"generic-api-key\""));
            Assert.That(config, Does.Contain("[[rules.allowlists]]"));
            Assert.That(config, Does.Contain(@"^Solution/config/security/test-access-keys\.json$"));
            Assert.That(config, Does.Contain(@"^Solution/scripts/local-e2e\.(sh|ps1)$"));
            Assert.That(config, Does.Contain(@"^Solution/install/windows-production/Jornada\.Cluster\.Test\.json$"));
            Assert.That(config, Does.Not.Contain(@"Solution/.*"),
                "Não permitir exceção ampla de diretório.");
            Assert.That(config, Does.Not.Contain(@"tests/.*"),
                "Não permitir exceção ampla de testes.");
        });
    }

    [Test]
    public void Local_secret_scan_reports_are_covered_by_gitignore()
    {
        var root = FindRepositoryRoot();
        var ignore = File.ReadAllText(Path.Combine(root, "Solution", ".gitignore"));
        Assert.That(ignore, Does.Match(new Regex(@"(?m)^\.local/\s*$")));
    }

    [Test]
    public void Security_ci_runs_checksum_pinned_gitleaks_on_current_tree()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));

        Assert.Multiple(() =>
        {
            Assert.That(workflow, Does.Contain("GITLEAKS_VERSION: '8.30.1'"));
            Assert.That(workflow, Does.Contain("GITLEAKS_LINUX_X64_SHA256: '551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb'"));
            Assert.That(workflow, Does.Contain("sha256sum --check --"));
            Assert.That(workflow, Does.Contain("./scripts/security-secret-scan.sh --current-tree-only"));
            Assert.That(workflow, Does.Contain(@"scan_rc=${PIPESTATUS[0]}"),
                "Mesmo quando o scanner falha, o CI deve copiar o relatório redigido antes de propagar o exit code.");
            Assert.That(workflow, Does.Not.Contain("gitleaks/gitleaks-action@"),
                "O CI usa binário versionado+checksum em vez de uma Action adicional não necessária.");
            Assert.That(workflow, Does.Not.Contain("gitleaks:latest"));
        });
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
