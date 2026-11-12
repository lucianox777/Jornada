using System.Text;
using Jornada.Linkage.Parameters.Worker;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class V9CalibrationJsonArtifactNameTests
{
    [Test]
    public void Filename_contains_full_sha_of_original_bytes_and_detects_modification()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"resultado\":1}\n");
        var date = new DateTimeOffset(2026, 11, 11, 15, 30, 0, TimeSpan.Zero);
        var name = V9CalibrationJsonArtifactName.Create(bytes, date);
        Assert.That(name, Does.Match(@"^calibracao_v9_20261111_153000_[a-f0-9]{64}\.json$"));
        Assert.That(V9CalibrationJsonArtifactName.Verify(name, bytes), Is.True);
        Assert.That(V9CalibrationJsonArtifactName.Verify(name, Encoding.UTF8.GetBytes("{\"resultado\":1}")), Is.False);
        Assert.That(V9CalibrationJsonArtifactName.Verify("../" + name, bytes), Is.False);
    }
    [Test]
    public void Hash_is_based_on_exact_bytes_not_semantically_equivalent_json()
    {
        var at = new DateTimeOffset(2026, 11, 11, 15, 30, 0, TimeSpan.Zero);
        var compact = Encoding.UTF8.GetBytes("""{"x":1}""");
        var formatted = Encoding.UTF8.GetBytes("""{ "x": 1 }""");
        var name = V9CalibrationJsonArtifactName.Create(compact, at);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(V9CalibrationJsonArtifactName.Verify(name, compact), Is.True);
            Assert.That(V9CalibrationJsonArtifactName.Verify(name, formatted), Is.False);
            Assert.That(V9CalibrationJsonArtifactName.Create(formatted, at), Is.Not.EqualTo(name));
            Assert.That(V9CalibrationJsonArtifactName.Verify("calibracao_v9_20261111_153000_invalid.json", compact), Is.False);
        }));
    }

    [Test]
    public void External_validation_emits_explicit_unbound_provenance_receipt()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, "Solution", "src",
            "Jornada.Linkage.Parameters.Worker", "V9NestedCalibrationCli.cs"));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(source, Does.Contain("finalist-validation-artifact-reference.json"));
            Assert.That(source, Does.Contain("artifactFileName = finalValidationName"));
            Assert.That(source, Does.Contain("artifactSha256 = Sha(finalValidationBytes)"));
            Assert.That(source, Does.Contain("sourceSelectionEnvelopeSha256 = selectionSha"));
            Assert.That(source, Does.Contain("modelId = (string?)null"));
            Assert.That(source, Does.Contain("EXTERNAL_VALIDATION_NOT_CERTIFIED_NOT_ACTIVATED"));
        }));
    }

    [Test]
    public void External_validation_checks_persisted_bytes_and_never_overwrites_artifacts()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, "Solution", "src",
            "Jornada.Linkage.Parameters.Worker", "V9NestedCalibrationCli.cs"));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(source, Does.Contain("FileMode.CreateNew"));
            Assert.That(source, Does.Contain("V9CalibrationJsonArtifactName.Verify(finalValidationName, storedBytes)"));
            Assert.That(source, Does.Contain("storedBytes.AsSpan().SequenceEqual(finalValidationBytes)"));
            Assert.That(source, Does.Contain("divergência entre SHA no nome e bytes persistidos"));
        }));
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RELEASE_INFO.txt")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Solution"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Raiz do Jornada não encontrada.");
    }

}
