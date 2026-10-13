using System.Text.Json;
using Jornada.Ingestion;
using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class PersonDocumentEvidenceTests
{
    private const string Example = """
        {
          "idPessoaEntrega":"P-DOC-1",
          "evidenciasDocumentais":[{
            "ocorrenciaOrigemCodigo":"SEHAB-DOC-001",
            "modeloDocumentoCodigo":"RG_ESTADUAL_TRADICIONAL",
            "modeloDocumentoVersao":1,
            "classeEvidencia":"DOCUMENTO",
            "dataEvidencia":"2020-07-12",
            "dataAtendimento":"2026-10-13T08:00:00-03:00",
            "valores":[
              {"atributoCodigo":"NOME_COMPLETO","valorOriginal":"Pessoa de Teste"},
              {"atributoCodigo":"DATA_NASCIMENTO","valorOriginal":"1980-01-01"}
            ]
          }]
        }
        """;

    [Test]
    public void All_four_active_Pessoa_v1_contracts_accept_occurrence_level_date_without_mandating_identity_fields()
    {
        var root = FindRoot();
        foreach (var gestor in new[] { "SEHAB", "SMADS", "SMDET", "SMS" })
        {
            var validator = JsonSchemaSubsetValidator.Load(Path.Combine(root, "Solution",
                "config", "contracts", "gestores", gestor, "pessoa", "v1", "pessoa.schema.json"));
            Assert.DoesNotThrow((Action)(() => validator.ParseAndValidate(Example, "pessoas.jsonl", 1)),
                gestor);
            Assert.DoesNotThrow((Action)(() => validator.ParseAndValidate(
                """{"idPessoaEntrega":"P-NO-DOC","nomeCompleto":null}""",
                "pessoas.jsonl", 2)), gestor);
            Assert.That((Func<object?>)(() => validator.ParseAndValidate(
                Example.Replace("2020-07-12", "2020-99-12", StringComparison.Ordinal),
                "pessoas.jsonl", 3)), Throws.TypeOf<InvalidDataException>(), gestor);
            Assert.That((Func<object?>)(() => validator.ParseAndValidate(
                Example.Replace("\"dataEvidencia\":\"2020-07-12\"",
                                "\"verificadoEm\":\"2026-10-13T08:00:00-03:00\"",
                                StringComparison.Ordinal),
                "pessoas.jsonl", 4)), Throws.TypeOf<InvalidDataException>(), gestor);
        }
    }

    [Test]
    public void Parser_preserves_proper_date_independent_of_attendance_and_one_date_for_two_values()
    {
        using var json = JsonDocument.Parse(Example);
        var parsed = PersonDocumentEvidenceParsing.Parse(json.RootElement).Single();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(parsed.EvidenceDate, Is.EqualTo(new DateOnly(2020, 7, 12)));
            Assert.That(parsed.AttendanceDate!.Value.Year, Is.EqualTo(2026));
            Assert.That(parsed.Values, Has.Count.EqualTo(2));
            Assert.That(parsed.ModelVersion, Is.EqualTo(1));
            Assert.That(parsed.EvidenceClass, Is.EqualTo("DOCUMENTO"));
        }));
        using var absent = JsonDocument.Parse(Example.Replace(
            "\"dataEvidencia\":\"2020-07-12\"",
            "\"dataEvidencia\":null", StringComparison.Ordinal));
        var unknown = PersonDocumentEvidenceParsing.Parse(absent.RootElement).Single();
        Assert.That(unknown.EvidenceDate, Is.Null,
            "A dataAtendimento não pode ser usada como substituto de dataEvidencia.");
    }

    [Test]
    public void Parser_fails_closed_for_duplicate_occurrences_values_and_empty_evidence()
    {
        using var one = JsonDocument.Parse(Example);
        var evidence = one.RootElement.GetProperty("evidenciasDocumentais")[0].GetRawText();
        using var duplicateOccurrence = JsonDocument.Parse(
            "{\"evidenciasDocumentais\":[" + evidence + "," + evidence + "]}");
        using var duplicateValue = JsonDocument.Parse(Example.Replace(
            "\"atributoCodigo\":\"DATA_NASCIMENTO\"",
            "\"atributoCodigo\":\"NOME_COMPLETO\"", StringComparison.Ordinal));
        using var empty = JsonDocument.Parse("""
            {"evidenciasDocumentais":[{
              "ocorrenciaOrigemCodigo":"NEW-1",
              "modeloDocumentoCodigo":"RG",
              "modeloDocumentoVersao":1,
              "classeEvidencia":"DOCUMENTO",
              "valores":[]
            }]}
            """);
        Assert.That((Action)(() => PersonDocumentEvidenceParsing.Parse(duplicateOccurrence.RootElement)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => PersonDocumentEvidenceParsing.Parse(duplicateValue.RootElement)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => PersonDocumentEvidenceParsing.Parse(empty.RootElement)),
            Throws.TypeOf<InvalidDataException>());
        using var invalidDate = JsonDocument.Parse(Example.Replace("2020-07-12", "2020-13-12"));
        Assert.That((Action)(() => PersonDocumentEvidenceParsing.Parse(invalidDate.RootElement)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Business_hash_changes_with_evidence_date_but_not_with_value_order()
    {
        using var original = JsonDocument.Parse(Example);
        using var changed = JsonDocument.Parse(Example.Replace("2020-07-12", "2021-07-12"));
        Assert.That(CanonicalJsonHash.ComputePerson(original.RootElement),
            Is.Not.EqualTo(CanonicalJsonHash.ComputePerson(changed.RootElement)));
        const string first = "{\"atributoCodigo\":\"NOME_COMPLETO\",\"valorOriginal\":\"Pessoa de Teste\"}";
        const string second = "{\"atributoCodigo\":\"DATA_NASCIMENTO\",\"valorOriginal\":\"1980-01-01\"}";
        var reversedJson = Example.Replace(first, "__TEMP_EVIDENCIA__",
                StringComparison.Ordinal)
            .Replace(second, first, StringComparison.Ordinal)
            .Replace("__TEMP_EVIDENCIA__", second, StringComparison.Ordinal);
        Assert.That(reversedJson, Is.Not.EqualTo(Example));
        using var reversed = JsonDocument.Parse(reversedJson);
        Assert.That(CanonicalJsonHash.ComputePerson(reversed.RootElement),
            Is.EqualTo(CanonicalJsonHash.ComputePerson(original.RootElement)));
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(dir.FullName, "Solution")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Raiz do Jornada não localizada.");
    }
}
