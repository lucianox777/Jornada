using Jornada.Ingestion;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class JsonSchemaSubsetValidatorTests
{
    [Test]
    public void Validates_required_pattern_and_conditional_rules()
    {
        var path = WriteSchema("""
        {
          "type":"object",
          "required":["codigo","cpf"],
          "properties":{
            "codigo":{"type":"string","pattern":"^[A-Z0-9]{4}$"},
            "cpf":{"type":["string","null"]},
            "cpfAusenteMotivo":{"type":["string","null"]}
          },
          "additionalProperties":false,
          "allOf":[{
            "if":{"properties":{"cpf":{"type":"null"}},"required":["cpf"]},
            "then":{"required":["cpfAusenteMotivo"],"properties":{"cpfAusenteMotivo":{"type":"string"}}},
            "else":{"properties":{"cpfAusenteMotivo":{"type":"null"}}}
          }]
        }
        """);
        try
        {
            var validator = JsonSchemaSubsetValidator.Load(path);
            Assert.DoesNotThrow((Action)(() => validator.ParseAndValidate("{\"codigo\":\"AA01\",\"cpf\":null,\"cpfAusenteMotivo\":\"SEM_CPF\"}", "pessoas.jsonl", 1)));
            Assert.That(() => validator.ParseAndValidate("{\"codigo\":\"AA001\",\"cpf\":null,\"cpfAusenteMotivo\":\"SEM_CPF\"}", "pessoas.jsonl", 2), Throws.TypeOf<InvalidDataException>());
            Assert.That(() => validator.ParseAndValidate("{\"codigo\":\"AA01\",\"cpf\":null}", "pessoas.jsonl", 3), Throws.TypeOf<InvalidDataException>());
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void Rejects_unknown_schema_keyword_instead_of_ignoring_it()
    {
        var path = WriteSchema("{\"type\":\"object\",\"unevaluatedProperties\":false}");
        try
        {
            Assert.That(() => JsonSchemaSubsetValidator.Load(path), Throws.TypeOf<InvalidDataException>());
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void Current_person_contract_accepts_documentary_conference_without_document_payload()
    {
        var validator = LoadPersonValidator();
        const string json = """
        {
          "idPessoaEntrega":"CURRENT-DOC-1",
          "codigoPessoaOrigem":"SMS001",
          "sourceTransactionId":"SMS-TX-1",
          "cpf":"11144477735",
          "cpfAusenteMotivo":null,
          "nomeCompleto":"Maria da Silva",
          "dataNascimento":"1982-04-10",
          "nomeMae":"Ana de Souza",
          "conferenciasDocumentais":[{
             "campoCodigo":"CPF",
             "evidenciaTipo":"DOCUMENTO_OFICIAL",
             "referenciaEvidencia":"ATEND-1",
             "verificadoEm":"2026-08-29T09:00:00-03:00"
          }]
        }
        """;
        Assert.DoesNotThrow((Action)(() => validator.ParseAndValidate(json, "pessoas.jsonl", 1)));
    }

    [Test]
    public void Current_person_contract_allows_geography_only_on_territorial_attributes()
    {
        var validator = LoadPersonValidator();
        const string valid = """
        {
          "idPessoaEntrega":"CURRENT-GEO-1",
          "codigoPessoaOrigem":"SMS-GEO-1",
          "cpf":"11144477735",
          "cpfAusenteMotivo":null,
          "nomeCompleto":"Maria da Silva",
          "dataNascimento":"1982-04-10",
          "nomeMae":"Ana de Souza",
          "atributosTransversais":[{
            "atributoCodigo":"ENDERECO_RESIDENCIAL",
            "valor":"CEP=01001000|NUMERO=100",
            "statusEvidencia":"DECLARADO",
            "situacaoGeografia":"RESOLVIDA",
            "geografia":{
              "distritoCodigo":"SE",
              "distritoNome":"Sé",
              "subprefeituraCodigo":"SE",
              "subprefeituraNome":"Sé",
              "referenciaMalha":"ORIGEM-2026"
            }
          }]
        }
        """;
        const string invalid = """
        {
          "idPessoaEntrega":"CURRENT-GEO-2",
          "codigoPessoaOrigem":"SMS-GEO-2",
          "cpf":"52998224725",
          "cpfAusenteMotivo":null,
          "nomeCompleto":"João da Silva",
          "dataNascimento":"1980-01-01",
          "nomeMae":"Ana da Silva",
          "atributosTransversais":[{
            "atributoCodigo":"TELEFONE_CONTATO",
            "valor":"11999999999",
            "statusEvidencia":"DECLARADO",
            "geografia":{
              "distritoCodigo":"SE",
              "distritoNome":"Sé",
              "subprefeituraCodigo":"SE",
              "subprefeituraNome":"Sé",
              "referenciaMalha":"ORIGEM-2026"
            }
          }]
        }
        """;
        Assert.DoesNotThrow((Action)(() => validator.ParseAndValidate(valid, "pessoas.jsonl", 1)));
        Assert.That(() => validator.ParseAndValidate(invalid, "pessoas.jsonl", 2), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Current_person_contract_accepts_declared_no_fixed_address_without_postal_address()
    {
        var validator = LoadPersonValidator();
        const string json = """
        {
          "idPessoaEntrega":"CURRENT-RT-1",
          "codigoPessoaOrigem":"RT-1",
          "cpf":null,
          "cpfAusenteMotivo":"NAO_INFORMADO_ORIGEM",
          "nomeCompleto":"Pessoa sem domicílio",
          "dataNascimento":null,
          "nomeMae":null,
          "atributosTransversais":[{
            "atributoCodigo":"REFERENCIA_TERRITORIAL",
            "statusEvidencia":"DECLARADO",
            "estadoReferenciaTerritorial":"SEM_ENDERECO_FIXO_DECLARADO"
          }]
        }
        """;
        Assert.DoesNotThrow((Action)(() => validator.ParseAndValidate(json, "pessoas.jsonl", 1)));
    }

    private static string WriteSchema(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"jornada-schema-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, text);
        return path;
    }

    private static JsonSchemaSubsetValidator LoadPersonValidator()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "config", "contracts", "gestores", "SMS", "pessoa", "v1", "pessoa.schema.json");
            if (File.Exists(candidate)) return JsonSchemaSubsetValidator.Load(candidate);
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Contrato Pessoa corrente v1 não localizado a partir do diretório de teste.");
    }
}
