namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class OriginalClusterReadinessContractTests
{
    [Test]
    public void Readiness_checks_original_DEV_and_shared_NAS_without_touching_operational_SQL()
    {
        var root = FindRoot();
        var script = File.ReadAllText(Path.Combine(root, "Solution", "scripts", "local-cluster-readiness.ps1"));
        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("Join-Path $Root '.env'"));
            Assert.That(script, Does.Contain("$values['JORNADA_SQL_DATABASE'] -ne $DatabaseName"));
            Assert.That(script, Does.Contain("Jornada.EnvironmentProfile"));
            Assert.That(script, Does.Contain("Jornada.SolutionSchema"));
            Assert.That(script, Does.Contain("configuration_bundle_version"));
            Assert.That(script, Does.Contain("heartbeat_em>=DATEADD(SECOND,-35"));
            Assert.That(script, Does.Contain("jornada-node1"));
            Assert.That(script, Does.Contain("jornada-node2"));
            Assert.That(script, Does.Contain("/data/bronze/$1"));
            Assert.That(script, Does.Contain("rm -f"));
            Assert.That(script, Does.Not.Contain("Jornada_Dev_SyntheticCalibration_Cleanup.sql"));
            Assert.That(script, Does.Not.Contain("local-synthetic-calibration.ps1"));
            Assert.That(script, Does.Not.Contain("DROP DATABASE"));
            Assert.That(script, Does.Not.Contain("--publish true"));
        });
    }
    private static string FindRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt")) &&
                Directory.Exists(Path.Combine(current.FullName, "Solution")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Raiz do repositório Jornada não encontrada.");
    }
}
