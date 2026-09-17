using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class LinkageParametersBootstrapProgressTests
{
    [Test]
    public void Program_EmitsHeartbeatWhileCanonicalNameSnapshotIsLoading()
    {
        var root = FindRepositoryRoot();
        var programPath = Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Linkage.Parameters.Worker",
            "Program.cs");
        var program = File.ReadAllText(programPath);

        Assert.Multiple(() =>
        {
            Assert.That(program, Does.Contain("RunHostWithHeartbeatAsync"));
            Assert.That(program, Does.Contain("TimeSpan.FromSeconds(15)"));
            Assert.That(program, Does.Contain("processo ativo, aguarde"));
            Assert.That(program, Does.Contain("milhões de linhas e pode levar alguns minutos"));
            Assert.That(program, Does.Contain("bootstrapBuilder.Build(),"));
            Assert.That(program, Does.Contain("operation == NameFrequencySnapshotLoader.Operation"));
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
