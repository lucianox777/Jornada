using System.IO.Compression;
using System.Text;
using Jornada.Ingestion;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class BasePessoaOrigemManifestTests
{
    [Test]
    public void Manifest_accepts_shared_person_base_code()
    {
        using var package = BuildPackage("CADASTRO_CIDADAO_COMPARTILHADO");

        var manifest = IngestionPackageInspector.ParseAndValidate(package, package.Length);

        Assert.That(manifest.CodigoBasePessoaOrigem, Is.EqualTo("CADASTRO_CIDADAO_COMPARTILHADO"));
    }

    [Test]
    public void Manifest_accepts_jornada_person_base_code()
    {
        using var package = BuildPackage("JORNADA");

        var manifest = IngestionPackageInspector.ParseAndValidate(package, package.Length);

        Assert.That(manifest.CodigoBasePessoaOrigem, Is.EqualTo("JORNADA"));
    }

    [Test]
    public void Manifest_keeps_legacy_compatibility_when_person_base_is_omitted()
    {
        using var package = BuildPackage(null);

        var manifest = IngestionPackageInspector.ParseAndValidate(package, package.Length);

        Assert.That(manifest.CodigoBasePessoaOrigem, Is.Null);
    }

    [TestCase("base-minuscula")]
    [TestCase("BASE COM ESPACO")]
    [TestCase("BASE.PESSOA")]
    public void Manifest_rejects_invalid_person_base_code(string code)
    {
        using var package = BuildPackage(code);

        var error = Assert.Throws<InvalidDataException>(() =>
            IngestionPackageInspector.ParseAndValidate(package, package.Length));

        Assert.That(error!.Message, Does.Contain("codigoBasePessoaOrigem"));
    }

    private static MemoryStream BuildPackage(string? baseCode)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var baseProperty = baseCode is null ? string.Empty : $",\"codigoBasePessoaOrigem\":\"{baseCode}\"";
            Write(zip, "manifest.json",
                "{\"formatoVersao\":2,\"pessoaSchemaVersao\":3,\"codigoSistemaOrigem\":\"SMADS\",\"natureza\":null,\"codigoTipo\":null,\"tipoVersao\":null,\"dataReferencia\":\"2026-09-13T12:00:00-03:00\"" + baseProperty + "}");
            Write(zip, "pessoas.jsonl", "{}\n");
            Write(zip, "registros.jsonl", string.Empty);
        }
        stream.Position = 0;
        return stream;
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
        writer.Write(text);
    }
}
