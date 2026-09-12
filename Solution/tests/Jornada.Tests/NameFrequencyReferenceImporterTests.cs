using System.Text.Json;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

[TestFixture]
public sealed class NameFrequencyReferenceImporterTests
{
    [Test]
    public void ParseRankingPage_reads_frequency_and_uses_identity_normalization()
    {
        using var document = JsonDocument.Parse("""
            {
              "totalPages": 3,
              "items": [
                { "nome": "Antônio", "frequencia": 12345, "rank": 1, "percent": 0.01 },
                { "nome": "Maria Clara", "frequencia": 2345, "rank": 2, "percent": 0.002 }
              ]
            }
            """);

        var page = NameFrequencyReferenceImporter.ParseRankingPage(document.RootElement, "NOME");

        Assert.Multiple(() =>
        {
            Assert.That(page.TotalPages, Is.EqualTo(3));
            Assert.That(page.Rows, Has.Count.EqualTo(2));
            Assert.That(page.Rows[0].Type, Is.EqualTo("NOME"));
            Assert.That(page.Rows[0].Value, Is.EqualTo("Antônio"));
            Assert.That(page.Rows[0].NormalizedValue, Is.EqualTo("ANTONIO"));
            Assert.That(page.Rows[0].Frequency, Is.EqualTo(12345));
            Assert.That(page.Rows[1].NormalizedValue, Is.EqualTo("MARIA CLARA"));
        });
    }

    [Test]
    public void ParseRankingPage_rejects_non_positive_frequency()
    {
        using var document = JsonDocument.Parse("""
            { "totalPages": 1, "items": [ { "nome": "Silva", "frequencia": 0 } ] }
            """);

        Assert.Throws<InvalidDataException>(() =>
            NameFrequencyReferenceImporter.ParseRankingPage(document.RootElement, "SOBRENOME"));
    }

    [Test]
    public void CanonicalHash_is_order_independent_but_content_sensitive()
    {
        var a = new NameFrequencyReferenceImporter.NameFrequencyImportRow("NOME", "Maria", "MARIA", 10);
        var b = new NameFrequencyReferenceImporter.NameFrequencyImportRow("SOBRENOME", "Silva", "SILVA", 20);

        var first = NameFrequencyReferenceImporter.ComputeCanonicalHash([a, b]);
        var reordered = NameFrequencyReferenceImporter.ComputeCanonicalHash([b, a]);
        var changed = NameFrequencyReferenceImporter.ComputeCanonicalHash([a, b with { Frequency = 21 }]);

        Assert.Multiple(() =>
        {
            Assert.That(reordered, Is.EqualTo(first));
            Assert.That(changed, Is.Not.EqualTo(first));
            Assert.That(first, Has.Length.EqualTo(32));
        });
    }
}
