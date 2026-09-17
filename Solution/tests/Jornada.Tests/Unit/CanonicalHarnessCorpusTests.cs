using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class CanonicalHarnessCorpusTests
{
    [Test]
    public void CiHarnessMustUseSameIbgeWeightedCorpusPipelineAsScaleHarness()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));

        var load = workflow.IndexOf("LOAD_NAME_FREQUENCY_SNAPSHOT", StringComparison.Ordinal);
        var manifest = workflow.IndexOf("data/reference/ibge-nomes-2022/manifest.json", StringComparison.Ordinal);
        var generate = workflow.IndexOf("database/Jornada_Dev_SyntheticScale.sql", StringComparison.Ordinal);
        var diversify = workflow.IndexOf("database/Jornada_Dev_SyntheticScale_Diversify.sql", StringComparison.Ordinal);
        var rebuild = workflow.IndexOf("REBUILD_LOCAL_BLOCKING", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(load, Is.GreaterThanOrEqualTo(0), "CI harness deve carregar o snapshot IBGE pelo loader canônico.");
            Assert.That(manifest, Is.GreaterThan(load), "Manifesto IBGE deve ser associado à carga canônica.");
            Assert.That(generate, Is.GreaterThan(manifest), "Massa base deve ser gerada após carregar a referência.");
            Assert.That(diversify, Is.GreaterThan(generate), "Diversificação IBGE deve ocorrer após a massa base.");
            Assert.That(rebuild, Is.GreaterThan(diversify), "Blocking deve ser materializado somente após a diversificação.");
            Assert.That(workflow, Does.Contain("CENSO2022_NOMES_BRASIL_V1"));
        });
    }

    [Test]
    public void CiHarnessMustFailWhenNoCandidateEscapesDeliberateStressFixture()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));

        Assert.Multiple(() =>
        {
            Assert.That(workflow, Does.Contain("SEM_CANDIDATO_%"));
            Assert.That(workflow, Does.Contain("n%10<>0"));
            Assert.That(workflow, Does.Contain("UNEXPECTED_NO_CANDIDATE"));
            Assert.That(workflow, Does.Contain("test \"$UNEXPECTED_NO_CANDIDATE\" = '0'"));
            Assert.That(workflow, Does.Contain("REPORTED_NO_CANDIDATE"));
            Assert.That(workflow, Does.Contain("ACTUAL_NO_CANDIDATE"));
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".github", "workflows")) &&
                Directory.Exists(Path.Combine(current.FullName, "Solution")))
                return current.FullName;

            current = current.Parent;
        }

        Assert.Fail("Raiz do repositório Jornada não encontrada.");
        return string.Empty;
    }
}
