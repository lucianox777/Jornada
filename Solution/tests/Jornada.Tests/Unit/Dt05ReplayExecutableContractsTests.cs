namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt05ReplayExecutableContractsTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "database", "migrations")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Solution root");
    }

    [Test]
    public void Schema_v3_preserves_v2_executable_contract_identities()
    {
        var root = Root();
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations",
            "20261002_Linkage_Replay_Executable_Contracts_DT05.sql"));
        var publisher = File.ReadAllText(Path.Combine(root, "src", "Jornada.Linkage.Runner",
            "Dt05ReplayManifestPublisher.cs"));
        var runner = File.ReadAllText(Path.Combine(root, "src", "Jornada.Linkage.Runner",
            "ProbabilisticLinkageBatchRunner.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(migration, Does.Contain("schema_version IN(1,2)"));
            Assert.That(migration, Does.Contain("schema v2 exige contratos executáveis exatos"));
            Assert.That(migration, Does.Contain("normalization_version"));
            Assert.That(migration, Does.Contain("resolution_catalog_version"));
            Assert.That(migration, Does.Contain("projection_schema_version"));
            Assert.That(migration, Does.Contain("projection_fingerprint_sha256"));
            Assert.That(publisher, Does.Contain("schema_version = 3"));
            Assert.That(runner, Does.Contain("IdentityComparison.NormalizationVersion"));
            Assert.That(runner, Does.Contain("PersonResolutionContractCatalog.CatalogVersion"));
            Assert.That(runner, Does.Contain("PersonResolutionProjectionContract.ValidateSupported"));
            Assert.That(runner, Does.Contain("modelo sem identidade exata da projeção"));
        });
    }
}
