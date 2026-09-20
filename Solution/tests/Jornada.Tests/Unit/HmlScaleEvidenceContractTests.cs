namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class HmlScaleEvidenceContractTests
{
    [Test]
    public void Hml_scale_evidence_is_fail_closed_and_never_activates_or_publishes()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Ensaio", "HmlScaleEvidenceRunner.cs"));
        var program = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Ensaio", "Program.cs"));
        var settings = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Ensaio", "appsettings.json"));

        Assert.Multiple(() =>
        {
            Assert.That(program, Does.Contain("HML_SCALE_EVIDENCE"));
            Assert.That(source, Does.Contain("AllowNonProductionWrites"));
            Assert.That(source, Does.Contain("EnvironmentProfile=HML"));
            Assert.That(source, Does.Contain("BaselineSha"));
            Assert.That(source, Does.Contain("Jornada.SolutionSchema=3.70"));
            Assert.That(source, Does.Contain("conteudo_sha256"));
            Assert.That(source, Does.Contain("GENERATE_DRAFT"));
            Assert.That(source, Does.Contain("VALIDATE"));
            Assert.That(source, Does.Contain("--mode MODEL_VALIDATION"));
            Assert.That(source, Does.Contain("--publish false"));
            Assert.That(source, Does.Contain("CONCLUIDO_SEM_PUBLICACAO"));
            Assert.That(source, Does.Contain("activeModelBefore"));
            Assert.That(source, Does.Contain("activeModelAfter"));
            Assert.That(source, Does.Contain("activateModel = false"));
            Assert.That(source, Does.Contain("publishLinkage = false"));
            Assert.That(source, Does.Contain("SHA256.HashData"));
            Assert.That(source, Does.Not.Contain("NewCalibrator(\"ACTIVATE\""));
            Assert.That(source, Does.Not.Contain("LOAD_NAME_FREQUENCY_SNAPSHOT"));
            Assert.That(source, Does.Not.Contain("Jornada_Dev_SyntheticScale"));
            Assert.That(source, Does.Not.Contain("local-db"));
            Assert.That(settings, Does.Contain("\"AllowNonProductionWrites\": false"));
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(current.FullName, "Solution"))
                && Directory.Exists(Path.Combine(current.FullName, "Documentos")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Raiz do repositório Jornada não encontrada.");
    }
}
