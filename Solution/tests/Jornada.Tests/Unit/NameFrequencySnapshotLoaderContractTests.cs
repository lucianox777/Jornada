using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class NameFrequencySnapshotLoaderContractTests
{
    [Test]
    public void CoverageInsert_UsesColumnsDeclaredByCoverageMigration()
    {
        var root = FindRepositoryRoot();
        var loaderPath = Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Linkage.Parameters.Worker",
            "NameFrequencySnapshotLoader.cs");
        var migrationPath = Path.Combine(
            root,
            "Solution",
            "database",
            "migrations",
            "20260912_Frequencia_Nomes_Cobertura.sql");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(loaderPath), Is.True);
            Assert.That(File.Exists(migrationPath), Is.True);
        });

        var loader = File.ReadAllText(loaderPath);
        var migration = File.ReadAllText(migrationPath);

        Assert.Multiple(() =>
        {
            Assert.That(migration, Does.Contain("ausencia_semantica NVARCHAR(40) NOT NULL"));
            Assert.That(migration, Does.Contain("origem_endpoint NVARCHAR(400) NOT NULL"));
            Assert.That(loader, Does.Contain("cobertura,ausencia_semantica,origem_endpoint"));
            Assert.That(loader, Does.Not.Contain("ausencia_significa"));
            Assert.That(loader, Does.Not.Contain("cobertura,ausencia_semantica,origem)"));
            Assert.That(loader, Does.Contain("SnapshotJsonOptions,").Or.Contain("SnapshotJsonOptions)"));
            Assert.That(loader, Does.Not.Contain("new JsonSerializerOptions { PropertyNameCaseInsensitive = true },"));
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