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
        var compact = Encoding.UTF8.GetBytes("{\\"x\\":1}");
        var formatted = Encoding.UTF8.GetBytes("{ \\"x\\": 1 }");
        var name = V9CalibrationJsonArtifactName.Create(compact, at);
        Assert.Multiple(() =>
        {
            Assert.That(V9CalibrationJsonArtifactName.Verify(name, compact), Is.True);
            Assert.That(V9CalibrationJsonArtifactName.Verify(name, formatted), Is.False);
            Assert.That(V9CalibrationJsonArtifactName.Create(formatted, at), Is.Not.EqualTo(name));
            Assert.That(V9CalibrationJsonArtifactName.Verify("calibracao_v9_20261111_153000_invalid.json", compact), Is.False);
        });
    }

}
