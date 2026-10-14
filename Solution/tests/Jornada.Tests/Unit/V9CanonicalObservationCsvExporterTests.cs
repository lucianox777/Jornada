using System.Globalization;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9CanonicalObservationCsvExporterTests
{
    [Test]
    public void Exports_canonical_names_with_multiple_surnames_without_duplicate_feature_exception()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var observations = new[]
        {
            new V9CanonicalObservationCsvExporter.Input(
                "1", "P1", "TRAIN", "G1",
                "Maria da Silva Oliveira", "Ana de Souza Lima",
                new DateOnly(1982, 4, 10), null),
            new V9CanonicalObservationCsvExporter.Input(
                "2", "P2", "VALIDATION", "G2",
                "João da Silva Neto", "Maria de Souza Oliveira",
                null, null)
        };
        Assert.DoesNotThrow(() =>
            V9CanonicalObservationCsvExporter.Export(writer, observations));
        var lines = writer.ToString().TrimEnd().Split('\n');
        Assert.That(lines, Has.Length.EqualTo(3));
        Assert.That(lines[0], Is.EqualTo(V9CanonicalObservationCsvExporter.Header));
        Assert.That(lines[1], Does.Contain("MARIA DA SILVA OLIVEIRA"));
        Assert.That(lines[1], Does.Contain("ANA DE SOUZA LIMA"));
        Assert.That(lines[2], Does.Contain("JOAO DA SILVA NETO"));
        Assert.That(lines[2], Does.Contain("MARIA DE SOUZA OLIVEIRA"));
    }

    [Test]
    public void Preserves_original_quoted_names_and_canonical_projected_columns()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        V9CanonicalObservationCsvExporter.Export(writer,
        [
            new V9CanonicalObservationCsvExporter.Input(
                "o1", "base1", "TRAIN", "G1",
                "Maria, da Silva", "Ana de Souza",
                new DateOnly(1990, 2, 1), null)
        ]);
        var lines = writer.ToString().TrimEnd().Split('\n');
        Assert.That(lines, Has.Length.EqualTo(2));
        Assert.That(lines[1], Does.StartWith("o1,base1,TRAIN,G1,\"Maria, da Silva\""));
        Assert.That(lines[1], Does.Contain(",1990-02-01,"));
    }
}
