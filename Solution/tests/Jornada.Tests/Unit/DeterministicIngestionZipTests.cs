using System.Text;
using Jornada.Ingestion;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class DeterministicIngestionZipTests
{
    [Test]
    public void Same_logical_package_bytes_generate_identical_zip_and_sha256()
    {
        var manifest = Encoding.UTF8.GetBytes("{\"formatoVersao\":2,\"codigoSistemaOrigem\":\"TESTE\",\"pessoaSchemaVersao\":1,\"dataReferencia\":\"2026-08-29\"}");
        var pessoas = Encoding.UTF8.GetBytes("{\"codigoPessoaOrigem\":\"P1\",\"atributos\":[]}\n");
        var registros = Array.Empty<byte>();

        var first = DeterministicIngestionZipWriter.Create(manifest, pessoas, registros);
        var second = DeterministicIngestionZipWriter.Create(manifest, pessoas, registros);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(IngestionPackageInspector.ComputeSha256(second), Is.EqualTo(IngestionPackageInspector.ComputeSha256(first)));
        });
    }

    [Test]
    public void Canonical_zip_has_fixed_entry_order_and_timestamp()
    {
        var manifest = Encoding.UTF8.GetBytes("{\"formatoVersao\":2,\"codigoSistemaOrigem\":\"TESTE\",\"pessoaSchemaVersao\":1,\"dataReferencia\":\"2026-08-29\"}");
        var pessoas = Encoding.UTF8.GetBytes("{\"codigoPessoaOrigem\":\"P1\",\"atributos\":[]}\n");
        var zipBytes = DeterministicIngestionZipWriter.Create(manifest, pessoas, Array.Empty<byte>());

        using var ms = new MemoryStream(zipBytes, writable: false);
        using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
        Assert.That(zip.Entries.Select(e => e.FullName).ToArray(), Is.EqualTo(new[] { "manifest.json", "pessoas.jsonl", "registros.jsonl" }));
        Assert.That(
            zip.Entries.All(e => e.LastWriteTime.DateTime == DeterministicIngestionZipWriter.CanonicalEntryTimestamp.DateTime),
            Is.True);
    }
}
