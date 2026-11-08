namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class TerritorialDeveloperOriginExtensionTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Jornada.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Solution root not found.");
    }

    [Test]
    public void Extension_is_separate_from_sealed_v370_manifest()
    {
        var db = Path.Combine(Root(), "database");
        var manifest = File.ReadAllText(Path.Combine(db, "migrations", "manifest.txt"));
        var extension = Path.Combine(db, "postrelease", "v3.71",
            "20261107_Territorial_Origem_Desenvolvedor.sql");
        var migration = File.ReadAllText(extension);
        var current = "ENRIQUECIMENTO_DESENVOLVEDOR";
        var legacy = "ENRIQUECIMENTO_" + "PRO" + "DAM";
        Assert.Multiple((Action)(() =>
        {
            Assert.That(manifest, Does.Not.Contain("20261107_Territorial_Origem_Desenvolvedor.sql"));
            Assert.That(migration, Does.Contain(current));
            Assert.That(migration, Does.Not.Contain(legacy));
            Assert.That(migration, Does.Contain("BEGIN TRANSACTION"));
            Assert.That(migration, Does.Contain("COMMIT TRANSACTION"));
            Assert.That(migration, Does.Contain("ROLLBACK TRANSACTION"));
            Assert.That(migration, Does.Contain("WITH CHECK ADD CONSTRAINT"));
            Assert.That(migration, Does.Contain("UPDATE silver.referencia_territorial_observacao"));
            Assert.That(migration, Does.Contain("SET origem_geografia = @new"));
        }));
    }

    [Test]
    public void Fresh_and_upgrade_installers_link_canonical_370_and_new_overlay()
    {
        var db = Path.Combine(Root(), "database");
        var fresh = File.ReadAllText(Path.Combine(db, "Jornada_Fase1_v3.71_fresh.sql"));
        var upgrade = File.ReadAllText(Path.Combine(db, "Jornada_Fase1_v3.71_upgrade.sql"));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(fresh, Does.Contain(":r database/Jornada_Fase1_v3.70.sql"));
            Assert.That(fresh, Does.Contain(":r database/postrelease/v3.71/20261107_Territorial_Origem_Desenvolvedor.sql"));
            Assert.That(upgrade, Does.Not.Contain(":r database/Jornada_Fase1_v3.70.sql"));
            Assert.That(upgrade, Does.Contain(":r database/postrelease/v3.71/20261107_Territorial_Origem_Desenvolvedor.sql"));
        }));
    }
}
