using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class TestProjectBoundaryTests
{
    [Test]
    public void Unit_project_must_not_contain_integration_category_tests()
    {
        var root = FindRepositoryRoot();
        var unitProject = Path.Combine(root, "Solution", "tests", "Jornada.Tests");
        var offenders = Directory.EnumerateFiles(unitProject, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("[Category(\"Integration\")]", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty,
            "Testes de integração devem residir exclusivamente em Jornada.Integration.Tests: " + string.Join(", ", offenders));
    }

    [Test]
    public void Integration_project_must_classify_integration_and_external_real_data_fixtures_explicitly()
    {
        var root = FindRepositoryRoot();
        var integrationProject = Path.Combine(root, "Solution", "tests", "Jornada.Integration.Tests");
        var testFixtures = Directory.EnumerateFiles(integrationProject, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .Where(item => item.Text.Contains("[TestFixture", StringComparison.Ordinal))
            .ToArray();

        var unclassified = testFixtures
            .Where(item =>
                !item.Text.Contains("[Category(\"Integration\")]", StringComparison.Ordinal) &&
                !item.Text.Contains("[Category(\"ExternalRealData\")]", StringComparison.Ordinal))
            .Select(item => Path.GetRelativePath(root, item.Path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var externalOutsideDedicatedFolder = testFixtures
            .Where(item => item.Text.Contains("[Category(\"ExternalRealData\")]", StringComparison.Ordinal))
            .Where(item => !item.Path.Contains(
                Path.DirectorySeparatorChar + "ExternalRealData" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            .Select(item => Path.GetRelativePath(root, item.Path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(unclassified, Is.Empty,
                "Todo TestFixture do projeto Integration deve declarar Integration ou ExternalRealData: " + string.Join(", ", unclassified));
            Assert.That(externalOutsideDedicatedFolder, Is.Empty,
                "ExternalRealData deve permanecer fisicamente isolado em pasta própria: " + string.Join(", ", externalOutsideDedicatedFolder));
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt")) &&
                Directory.Exists(Path.Combine(current.FullName, "Solution")))
                return current.FullName;
            current = current.Parent;
        }

        Assert.Fail("Raiz do repositório Jornada não encontrada.");
        return string.Empty;
    }
}
