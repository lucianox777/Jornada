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
