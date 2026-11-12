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
}
