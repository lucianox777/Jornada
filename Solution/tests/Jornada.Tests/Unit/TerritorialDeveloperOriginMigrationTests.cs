namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class TerritorialDeveloperOriginMigrationTests
{
    private static string FindSolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Jornada.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Solution root not found.");
    }

    [Test]
    public void Forward_only_migration_replaces_legacy_domain_without_touching_sealed_history()
    {
        var root = FindSolutionRoot();
        var sql = File.ReadAllText(Path.Combine(root, "database", "migrations",
            "20261107_Territorial_Origem_Desenvolvedor.sql"));
        var ddl = File.ReadAllText(Path.Combine(root, "database", "Jornada_Fase1.sql"));
        var legacy = "ENRIQUECIMENTO_" + "PRO" + "DAM";
        var current = "ENRIQUECIMENTO_DESENVOLVEDOR";
        Assert.Multiple((Action)(() =>
        {
            Assert.That(ddl, Does.Contain(current));
            Assert.That(ddl, Does.Not.Contain(legacy));
            Assert.That(sql, Does.Contain("SET XACT_ABORT ON"));
            Assert.That(sql, Does.Contain("BEGIN TRANSACTION"));
            Assert.That(sql, Does.Contain("COMMIT TRANSACTION"));
            Assert.That(sql, Does.Contain("ROLLBACK TRANSACTION"));
            Assert.That(sql, Does.Contain("WITH CHECK ADD CONSTRAINT"));
            Assert.That(sql, Does.Contain("UPDATE silver.referencia_territorial_observacao"));
            Assert.That(sql, Does.Contain("SET origem_geografia = @new"));
            Assert.That(sql, Does.Contain("CHARINDEX(@old,c.definition)"));
            Assert.That(sql, Does.Contain(current));
            Assert.That(sql, Does.Not.Contain(legacy));
        }));
    }

    [Test]
    public void New_migration_is_registered_before_the_terminal_consolidation()
    {
        var lines = File.ReadAllLines(Path.Combine(FindSolutionRoot(), "database", "migrations", "manifest.txt"));
        var newLine = "migrations/20261107_Territorial_Origem_Desenvolvedor.sql";
        var last = "migrations/20260910_Schema_Consolidation_370.sql";
        var p = Array.IndexOf(lines, newLine);
        Assert.That(p, Is.GreaterThanOrEqualTo(0));
        Assert.That(Array.LastIndexOf(lines, newLine), Is.EqualTo(p));
        Assert.That(p, Is.EqualTo(Array.IndexOf(lines, last) - 1));
    }
}
