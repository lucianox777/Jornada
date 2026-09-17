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

    [Test]
    public void CanonicalIbgeReference_IsPreloadedAsEnvironmentBootstrap()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Linkage.Parameters.Worker",
            "Program.cs"));
        var entrypoint = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "install",
            "container-test",
            "entrypoint.sh"));
        var calibration = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "install",
            "windows-production",
            "Invoke-JornadaLinkageCalibration.ps1"));

        const string ensureOperation = "ENSURE_NAME_FREQUENCY_SNAPSHOT";
        const string canonicalReference = "CENSO2022_NOMES_BRASIL_V1";

        var ensureIndex = entrypoint.IndexOf(ensureOperation, StringComparison.Ordinal);
        var residentLoopIndex = entrypoint.IndexOf("while IFS= read -r task_json", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(program, Does.Contain($"const string EnsureNameFrequencySnapshotOperation = \"{ensureOperation}\""));
            Assert.That(program, Does.Contain($"const string CanonicalNameFrequencyReferenceCode = \"{canonicalReference}\""));
            Assert.That(program, Does.Contain("HasPublishedNameFrequencyReferenceAsync"));
            Assert.That(program, Does.Contain("nenhuma recarga necessária"));
            Assert.That(entrypoint, Does.Contain("[[ \"$JORNADA_NODE_ID\" == \"NODE2\" ]]"));
            Assert.That(ensureIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(residentLoopIndex, Is.GreaterThan(ensureIndex), "NODE2 deve materializar a referência antes dos processos residentes.");
            Assert.That(calibration, Does.Contain($"Invoke-Parameters '{ensureOperation}'"));
            Assert.That(calibration, Does.Not.Contain("Invoke-Parameters 'LOAD_NAME_FREQUENCY_SNAPSHOT'"));
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
