using System.Text.RegularExpressions;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class LinkageParameterCatalogTests
{
    [Test]
    public void Catalog_has_unique_parameter_names()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LinkageParameterCatalog.CoreScoringRequired.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(LinkageParameterCatalog.CoreScoringRequired.Count));
            Assert.That(LinkageParameterCatalog.BirthComponentRequired.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(LinkageParameterCatalog.BirthComponentRequired.Count));
            Assert.That(LinkageParameterCatalog.CalibrationValidationRequired.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(LinkageParameterCatalog.CalibrationValidationRequired.Count));
        });
    }

    [Test]
    public void Estimator_emits_every_catalogued_scoring_parameter()
    {
        var matched = new[]
        {
            new IdentityTrainingPair("Maria Silva", new DateOnly(1980, 1, 1), "Ana Silva",
                "Maria Silva", new DateOnly(1980, 1, 1), "Ana Silva")
        };
        var unmatched = new[]
        {
            new IdentityTrainingPair("Maria Silva", new DateOnly(1980, 1, 1), "Ana Silva",
                "Carlos Souza", new DateOnly(1981, 2, 2), "Lucia Souza")
        };

        var parameters = LinkageParameterEstimator.Estimate(matched, unmatched, 100, 50, 0.5m, 0.95m, 0.03m);

        Assert.Multiple(() =>
        {
            foreach (var name in LinkageParameterCatalog.CoreScoringRequired)
                Assert.That(parameters.ContainsKey(name), Is.True, $"Estimator não emitiu {name}.");
            foreach (var name in LinkageParameterCatalog.BirthComponentRequired)
                Assert.That(parameters.ContainsKey(name), Is.True, $"Estimator não emitiu {name}.");
            Assert.That(parameters.ContainsKey(LinkageParameterCatalog.BirthComponentScoring), Is.True);
        });
    }

    [Test]
    public void Sql_validation_required_list_matches_catalog()
    {
        var root = FindRepositoryRoot();
        var workerPath = Path.Combine(root, "src", "Jornada.Linkage.Parameters.Worker", "LinkageParametersWorker.cs");
        var source = File.ReadAllText(workerPath);
        var start = source.IndexOf("FROM (VALUES", StringComparison.Ordinal);
        var end = source.IndexOf(") req(nome)", start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));

        var valuesBlock = source[start..end];
        var actual = Regex.Matches(valuesBlock, "\\('([^']+)'\\)")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var expected = LinkageParameterCatalog.CalibrationValidationRequired.ToHashSet(StringComparer.Ordinal);

        Assert.That(actual, Is.EquivalentTo(expected));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            var solutionDirectory = Path.Combine(directory.FullName, "Solution");
            if (File.Exists(Path.Combine(solutionDirectory, "Jornada.sln")))
                return solutionDirectory;
            if (File.Exists(Path.Combine(directory.FullName, "Jornada.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Raiz da Solution não encontrada a partir do diretório de testes.");
    }
}
