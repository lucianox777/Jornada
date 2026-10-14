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
        Assert.DoesNotThrow((Action)(() =>
            V9CanonicalObservationCsvExporter.Export(writer, observations)));
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
    [Test]
    public void Opt_in_canonical_surname_csv_has_separate_last_and_any_person_mother_features()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        V9CanonicalObservationCsvExporter.ExportWithSurnames(writer,
        [
            new V9CanonicalObservationCsvExporter.Input(
                "o1", "P1", "TRAIN", "G1", "Maria da Silva Oliveira",
                "Ana de Souza Lima", new DateOnly(1980, 6, 5), null),
            new V9CanonicalObservationCsvExporter.Input(
                "o2", "P2", "VALIDATION", "G2", "João Silva Filho",
                "Maria", null, null)
        ]);
        using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(
            new StringReader(writer.ToString()));
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        var header = parser.ReadFields()!;
        Assert.That(string.Join(",", header),
            Is.EqualTo(V9CanonicalObservationCsvExporter.SurnameHeader));
        var first = parser.ReadFields()!;
        Assert.That(first, Has.Length.EqualTo(16));
        Assert.That(first[12], Is.EqualTo("OLIVEIRA"));
        Assert.That(System.Text.Json.JsonSerializer.Deserialize<string[]>(first[13]), Is.EqualTo(new[] { "OLIVEIRA", "SILVA" }));
        Assert.That(first[14], Is.EqualTo("LIMA"));
        Assert.That(System.Text.Json.JsonSerializer.Deserialize<string[]>(first[15]), Is.EqualTo(new[] { "LIMA", "SOUZA" }));
        var second = parser.ReadFields()!;
        Assert.That(second[12], Is.EqualTo("SILVA"));
        Assert.That(System.Text.Json.JsonSerializer.Deserialize<string[]>(second[13]), Is.EqualTo(new[] { "SILVA" }));
        Assert.That(second[14], Is.Empty);
        Assert.That(second[15], Is.EqualTo("[]"));
        Assert.That(parser.EndOfData, Is.True);
    }

    [Test]
    public void Legacy_canonical_csv_keeps_original_header_and_four_projected_columns()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        V9CanonicalObservationCsvExporter.Export(writer,
        [
            new V9CanonicalObservationCsvExporter.Input(
                "x", "P", "TRAIN", "G", "Maria Silva Oliveira",
                "Ana Souza Lima", null, null)
        ]);
        using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(
            new StringReader(writer.ToString()));
        parser.SetDelimiters(",");
        var header = parser.ReadFields()!;
        Assert.That(header, Has.Length.EqualTo(12));
        Assert.That(string.Join(",", header),
            Is.EqualTo(V9CanonicalObservationCsvExporter.Header));
        Assert.That(parser.ReadFields(), Has.Length.EqualTo(12));
    }

}
