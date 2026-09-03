using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Ingestion;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class IngestionContractTests
{
    [Test]
    public void Manifest_uses_single_delivery_envelope_without_external_version_or_family()
    {
        var manifest = new IngestionPackageManifest(2, 1, "HABITACAO", IntegrationNature.BENEFICIO, "AA01", 1, DateTimeOffset.UtcNow);
        var names = typeof(IngestionPackageManifest).GetProperties().Select(p => p.Name).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(Enum.GetNames<IntegrationNature>(), Is.EquivalentTo(new[] { "BENEFICIO", "SERVICO" }));
            Assert.That(manifest.CodigoTipo, Has.Length.EqualTo(4));
            Assert.That(manifest.CodigoSistemaOrigem, Is.EqualTo("HABITACAO"));
            Assert.That(names, Does.Not.Contain("FamiliaEntrega"));
            Assert.That(names, Does.Not.Contain("VersaoRegistroOrigem"));
            Assert.That(names, Does.Not.Contain("EntregaId"));
            Assert.That(names, Does.Not.Contain("LoteSeq"));
            Assert.That(names, Does.Not.Contain("LoteTotal"));
        });
    }

    [TestCase("AA01")]
    [TestCase("POT1")]
    [TestCase("CRA1")]
    public void Sample_type_codes_are_four_alphanumeric_characters(string code) =>
        Assert.That(code, Does.Match("^[A-Z0-9]{4}$"));

    [Test]
    public void Cadastro_only_zip_is_valid_with_empty_registros_and_no_fact_context()
    {
        var bytes = BuildZip(new Dictionary<string,string>
        {
            ["manifest.json"] = CadastroManifest(),
            ["pessoas.jsonl"] = "{\"codigoPessoaOrigem\":\"1\",\"nomeCompleto\":\"Maria\"}\n",
            ["registros.jsonl"] = string.Empty
        });
        var manifest = IngestionPackageInspector.ParseAndValidate(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(manifest.Natureza, Is.Null);
            Assert.That(manifest.CodigoTipo, Is.Null);
        });
    }

    [Test]
    public void Typed_delivery_can_explicitly_confirm_zero_facts_in_period()
    {
        var bytes = BuildZip(new Dictionary<string,string>
        {
            ["manifest.json"] = FactualManifest("SERVICO", "CRA1"),
            ["pessoas.jsonl"] = "{}\n",
            ["registros.jsonl"] = string.Empty
        });
        Assert.DoesNotThrow(() => IngestionPackageInspector.ParseAndValidate(bytes));
    }

    [Test]
    public void Non_empty_registros_requires_complete_fact_context()
    {
        var bytes = BuildZip(new Dictionary<string,string>
        {
            ["manifest.json"] = CadastroManifest(),
            ["pessoas.jsonl"] = "{}\n",
            ["registros.jsonl"] = "{}\n"
        });
        Assert.That(() => IngestionPackageInspector.ParseAndValidate(bytes), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Partial_fact_context_is_rejected_even_when_registros_is_empty()
    {
        var manifest = "{\"formatoVersao\":2,\"pessoaSchemaVersao\":1,\"codigoSistemaOrigem\":\"SAUDE\",\"natureza\":\"SERVICO\",\"codigoTipo\":null,\"tipoVersao\":null,\"dataReferencia\":\"2026-08-28T00:00:00-03:00\"}";
        var bytes = BuildZip(new Dictionary<string,string>
        {
            ["manifest.json"] = manifest,
            ["pessoas.jsonl"] = "{}\n",
            ["registros.jsonl"] = string.Empty
        });
        Assert.That(() => IngestionPackageInspector.ParseAndValidate(bytes), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Zip_bomb_with_declared_uncompressed_size_over_two_gib_is_rejected_before_payload_read()
    {
        var bytes = BuildZip(new Dictionary<string,string>
        {
            ["manifest.json"] = FactualManifest("BENEFICIO", "AA01"),
            ["pessoas.jsonl"] = "{}\n",
            ["registros.jsonl"] = "{}\n"
        });
        PatchCentralDirectoryUncompressedSize(bytes, "pessoas.jsonl", 0x80000001u);
        Assert.That(() => IngestionPackageInspector.ParseAndValidate(bytes), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Actual_uncompressed_bytes_are_counted_instead_of_trusting_only_central_directory()
    {
        var bytes = BuildZip(new Dictionary<string,string>
        {
            ["manifest.json"] = CadastroManifest(),
            ["pessoas.jsonl"] = new string('x', 4096),
            ["registros.jsonl"] = string.Empty
        });
        using var ms = new MemoryStream(bytes, writable: false);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.That(() => IngestionPackageInspector.ValidateActualUncompressedSize(zip, 1024), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Subdirectory_or_traversal_entry_is_rejected()
    {
        var bytes = BuildZip(new Dictionary<string,string>
        {
            ["manifest.json"] = FactualManifest("BENEFICIO", "AA01"),
            ["pessoas.jsonl"] = "{}\n",
            ["../registros.jsonl"] = "{}\n"
        });
        Assert.That(() => IngestionPackageInspector.ParseAndValidate(bytes), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Legacy_fact_file_name_is_rejected()
    {
        var bytes = BuildZip(new Dictionary<string,string>
        {
            ["manifest.json"] = FactualManifest("BENEFICIO", "AA01"),
            ["pessoas.jsonl"] = "{}\n",
            ["beneficios_concedidos.jsonl"] = "{}\n"
        });
        Assert.That(() => IngestionPackageInspector.ParseAndValidate(bytes), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Empty_pessoas_jsonl_is_rejected()
    {
        var bytes = BuildZip(new Dictionary<string,string>
        {
            ["manifest.json"] = CadastroManifest(),
            ["pessoas.jsonl"] = string.Empty,
            ["registros.jsonl"] = string.Empty
        });
        Assert.That(() => IngestionPackageInspector.ParseAndValidate(bytes), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Canonical_filename_must_contain_actual_sha256()
    {
        var bytes = BuildZip(new Dictionary<string,string>
        {
            ["manifest.json"] = FactualManifest("SERVICO", "CRA1"),
            ["pessoas.jsonl"] = "{}\n",
            ["registros.jsonl"] = "{}\n"
        });
        var manifest = IngestionPackageInspector.ParseAndValidate(bytes);
        var hash = IngestionPackageInspector.ComputeSha256(bytes);
        var context = new AccessContext(Guid.NewGuid(), AccessCredentialType.GESTOR, "SMADS", "SMADS", null, [], []);
        Assert.DoesNotThrow(() => IngestionPackageInspector.ValidateCanonicalFileName($"ENTREGA_SMADS_ASSISTENCIA_v2_{hash}.zip", manifest, context, hash));
        var ex = Assert.Throws<InvalidDataException>(() =>
            IngestionPackageInspector.ValidateCanonicalFileName($"ENTREGA_SMADS_ASSISTENCIA_v2_{new string('0',64)}.zip", manifest, context, hash));
        Assert.That(ex!.Message, Does.Not.Contain(hash));
    }

    [Test]
    public void Canonical_filename_rejects_path_traversal()
    {
        var manifest = new IngestionPackageManifest(2, 1, "SAUDE", null, null, null, DateTimeOffset.UtcNow);
        var context = new AccessContext(Guid.NewGuid(), AccessCredentialType.GESTOR, "SMS", "SMS", null, [], []);
        var hash = new string('a', 64);
        Assert.That(() => IngestionPackageInspector.ValidateCanonicalFileName($"../ENTREGA_SMS_SAUDE_v2_{hash}.zip", manifest, context, hash), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Required_entry_names_are_case_sensitive_and_canonical()
    {
        var bytes = BuildZip(new Dictionary<string,string>
        {
            ["MANIFEST.JSON"] = CadastroManifest(),
            ["pessoas.jsonl"] = "{}\n",
            ["registros.jsonl"] = string.Empty
        });
        Assert.That(() => IngestionPackageInspector.ParseAndValidate(bytes), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Duplicate_entry_names_are_rejected_case_insensitively()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "manifest.json", CadastroManifest());
            WriteEntry(zip, "pessoas.jsonl", "{}\n");
            WriteEntry(zip, "PESSOAS.JSONL", "{}\n");
            WriteEntry(zip, "registros.jsonl", string.Empty);
        }
        Assert.That(() => IngestionPackageInspector.ParseAndValidate(ms.ToArray()), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Journey_history_is_common_but_benefit_and_service_details_are_specialized()
    {
        var uuid = Guid.NewGuid();
        var registro = new RegistroJornadaDto("S:1", uuid, "SERVICO", "CRA1", "Serviço CRAS", "SMADS", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "REALIZADO");
        var servico = new ServicoPrestadoPessoaDto(1, uuid, "SMADS", "CRA1", "Serviço CRAS", 1, 1, "INCLUSAO", "VIGENTE", "CRA-1", DateTimeOffset.UtcNow, "CRAS Sé", "REALIZADO", DateTimeOffset.UtcNow);
        Assert.That(registro.Natureza, Is.EqualTo("SERVICO"));
        Assert.That(servico.UnidadeServico, Is.EqualTo("CRAS Sé"));
    }

    [Test]
    public void Person_projection_exposes_gold_interpretation_metadata_outside_variable_schema()
    {
        var metadata = new PersonProjectionMetadata(3, "CORROBORADO", DateTimeOffset.Parse("2026-08-30T12:00:00-03:00", System.Globalization.CultureInfo.InvariantCulture));
        using var document = JsonDocument.Parse("""{"nomeCompleto":"Maria da Silva"}""");
        var response = new PersonProjectionResponse(Guid.NewGuid(), document.RootElement.Clone(), "pessoa.schema.json", metadata);

        Assert.Multiple(() =>
        {
            Assert.That(response.Metadados.FontesDistintas, Is.EqualTo(3));
            Assert.That(response.Metadados.EstadoConcordancia, Is.EqualTo("CORROBORADO"));
            Assert.That(response.Dados.TryGetProperty("fontesDistintas", out _), Is.False);
        });
    }

    [Test]
    public void Fact_dtos_do_not_expose_generic_specific_attributes_json()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(BeneficioConcedidoPessoaDto).GetProperties().Any(p => p.Name.Contains("Atribut", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(typeof(ServicoPrestadoPessoaDto).GetProperties().Any(p => p.Name.Contains("Atribut", StringComparison.OrdinalIgnoreCase)), Is.False);
        });
    }

    [Test]
    public void Fixture_ingestion_packages_are_valid_contract_inputs()
    {
        var root = Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures", "ingestao");
        var directories = new[] { "CADASTRO_SMS_v2", "AA01_v2", "AA01_SEM_FATOS_v2", "CRA1_v2" };
        foreach (var directory in directories)
        {
            var path = Path.Combine(root, directory);
            Assert.That(Directory.Exists(path), Is.True, $"Fixture ausente: {directory}");
            var files = Directory.GetFiles(path).ToDictionary(
                file => Path.GetFileName(file)
                    ?? throw new InvalidDataException($"Não foi possível obter o nome do arquivo: {file}"),
                File.ReadAllText,
                StringComparer.Ordinal);
            var bytes = BuildZip(files);
            Assert.DoesNotThrow(() => IngestionPackageInspector.ParseAndValidate(bytes), directory);
        }
    }

    private static string CadastroManifest() =>
        "{\"formatoVersao\":2,\"pessoaSchemaVersao\":1,\"codigoSistemaOrigem\":\"SAUDE\",\"natureza\":null,\"codigoTipo\":null,\"tipoVersao\":null,\"dataReferencia\":\"2026-08-28T00:00:00-03:00\"}";

    private static string FactualManifest(string natureza, string codigo)
    {
        var sistema = natureza == "SERVICO" ? "ASSISTENCIA" : "HABITACAO";
        return $"{{\"formatoVersao\":2,\"pessoaSchemaVersao\":1,\"codigoSistemaOrigem\":\"{sistema}\",\"natureza\":\"{natureza}\",\"codigoTipo\":\"{codigo}\",\"tipoVersao\":1,\"dataReferencia\":\"2026-08-28T00:00:00-03:00\"}}";
    }

    private static byte[] BuildZip(IReadOnlyDictionary<string,string> files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var pair in files)
                WriteEntry(zip, pair.Key, pair.Value);
        }
        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, string value)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(value);
    }

    private static void PatchCentralDirectoryUncompressedSize(byte[] zip, string entryName, uint declaredSize)
    {
        var name = Encoding.UTF8.GetBytes(entryName);
        for (var i = 0; i <= zip.Length - 46 - name.Length; i++)
        {
            if (zip[i] != 0x50 || zip[i+1] != 0x4b || zip[i+2] != 0x01 || zip[i+3] != 0x02) continue;
            var nameLen = BitConverter.ToUInt16(zip, i + 28);
            if (nameLen != name.Length) continue;
            if (!zip.AsSpan(i + 46, name.Length).SequenceEqual(name)) continue;
            BitConverter.GetBytes(declaredSize).CopyTo(zip, i + 24);
            return;
        }
        throw new InvalidOperationException("Central directory entry not found.");
    }
    [Test]
    public void Minimal_manifest_read_supports_resource_authorization_before_full_payload_validation()
    {
        var bytes = BuildZip(new Dictionary<string,string>
        {
            ["manifest.json"] = """{"formatoVersao":2,"pessoaSchemaVersao":1,"codigoSistemaOrigem":"ASSISTENCIA","natureza":"BENEFICIO","codigoTipo":"AA01","tipoVersao":1,"dataReferencia":"2026-08-29T00:00:00-03:00"}""",
            ["pessoas.jsonl"] = "{}\n",
            ["registros.jsonl"] = "{}\n"
        });
        using var stream = new MemoryStream(bytes);
        var manifest = IngestionPackageInspector.ParseManifestForAuthorization(stream, bytes.LongLength);
        Assert.Multiple(() =>
        {
            Assert.That(manifest.CodigoTipo, Is.EqualTo("AA01"));
            Assert.That(manifest.CodigoSistemaOrigem, Is.EqualTo("ASSISTENCIA"));
        });
    }

}
