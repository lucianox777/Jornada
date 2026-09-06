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
            Assert.DoesNotThrow(() => validator.ParseAndValidate("{\"codigo\":\"AA01\",\"cpf\":null,\"cpfAusenteMotivo\":\"SEM_CPF\"}", "pessoas.jsonl", 1));
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
    public void Person_contract_accepts_documentary_conference_without_document_payload()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? schema = null;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "config", "contracts", "gestores", "SMS", "pessoa", "v1", "pessoa.schema.json");
            if (File.Exists(candidate)) { schema = candidate; break; }
            dir = dir.Parent;
        }
        Assert.That(schema, Is.Not.Null, "Contrato de Pessoa deve ser copiado para o diretório de teste; ausência é regressão da fixture.");
        var validator = JsonSchemaSubsetValidator.Load(schema!);
        const string json = """
        {
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
        Assert.DoesNotThrow(() => validator.ParseAndValidate(json, "pessoas.jsonl", 1));
    }


    [Test]
    public void Person_contract_allows_geography_only_on_residential_address()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? schema = null;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "config", "contracts", "gestores", "SMS", "pessoa", "v1", "pessoa.schema.json");
            if (File.Exists(candidate)) { schema = candidate; break; }
            dir = dir.Parent;
        }
        Assert.That(schema, Is.Not.Null, "Contrato de Pessoa deve ser copiado para o diretório de teste; ausência é regressão da fixture.");
        var validator = JsonSchemaSubsetValidator.Load(schema!);
        const string valid = """
        {
          "codigoPessoaOrigem":"SMS-GEO-1","cpf":"11144477735","cpfAusenteMotivo":null,
          "nomeCompleto":"Maria da Silva","dataNascimento":"1982-04-10","nomeMae":"Ana de Souza",
          "atributosTransversais":[{
            "atributoCodigo":"ENDERECO_RESIDENCIAL","valor":"CEP=01001000|NUMERO=100",
            "statusEvidencia":"DECLARADO","situacaoGeografia":"RESOLVIDA","geografia":{
              "distritoCodigo":"SE","distritoNome":"Sé",
              "subprefeituraCodigo":"SE","subprefeituraNome":"Sé","referenciaMalha":"ORIGEM-2026"
            }
          }]
        }
        """;
        const string invalid = """
        {
          "codigoPessoaOrigem":"SMS-GEO-2","cpf":"52998224725","cpfAusenteMotivo":null,
          "nomeCompleto":"João da Silva","dataNascimento":"1980-01-01","nomeMae":"Ana da Silva",
          "atributosTransversais":[{
            "atributoCodigo":"TELEFONE","valor":"11999999999","statusEvidencia":"DECLARADO",
            "geografia":{"distritoCodigo":"SE","distritoNome":"Sé","subprefeituraCodigo":"SE","subprefeituraNome":"Sé"}
          }]
        }
        """;
        Assert.DoesNotThrow(() => validator.ParseAndValidate(valid, "pessoas.jsonl", 1));
        Assert.That(() => validator.ParseAndValidate(invalid, "pessoas.jsonl", 2), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Person_v2_allows_cpf_as_source_code_fallback_but_rejects_missing_code_without_cpf()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? schema = null;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "config", "contracts", "gestores", "SEHAB", "pessoa", "v2", "pessoa.schema.json");
            if (File.Exists(candidate)) { schema = candidate; break; }
            dir = dir.Parent;
        }
        Assert.That(schema, Is.Not.Null, "Contrato Pessoa v2 da SEHAB deve integrar a fixture de testes.");
        var validator = JsonSchemaSubsetValidator.Load(schema!);
        const string valid = """
        {
          "cpf":"70819234532","cpfAusenteMotivo":null,"nomeCompleto":"Maria da Silva",
          "dataNascimento":"1982-04-10","nomeMae":"Ana de Souza"
        }
        """;
        const string invalid = """
        {
          "cpf":null,"cpfAusenteMotivo":"SEM_CPF","nomeCompleto":"Pessoa sem código",
          "dataNascimento":"1982-04-10","nomeMae":"Ana de Souza"
        }
        """;
        Assert.DoesNotThrow(() => validator.ParseAndValidate(valid, "pessoas.jsonl", 1));
        Assert.That(() => validator.ParseAndValidate(invalid, "pessoas.jsonl", 2), Throws.TypeOf<InvalidDataException>());
    }

    private static string WriteSchema(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"jornada-schema-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, text);
        return path;
    }

    [Test]
    public void Person_schema_accepts_declared_territorial_reference_without_postal_address()
    {
        var validator = LoadPersonValidator();
        const string json = """
        {
          "codigoPessoaOrigem":"RT-1","cpf":null,"cpfAusenteMotivo":"SEM_CPF","nomeCompleto":"Pessoa sem domicílio",
          "dataNascimento":"1990-01-01","nomeMae":"Maria",
          "atributosTransversais":[{
            "atributoCodigo":"REFERENCIA_TERRITORIAL","statusEvidencia":"DECLARADO",
            "naturezaReferenciaTerritorial":"REFERENCIA_TERRITORIAL_DECLARADA",
            "situacaoGeografia":"RESOLVIDA",
            "geografia":{"distritoCodigo":"SE","distritoNome":"Sé","subprefeituraCodigo":"SE","subprefeituraNome":"Sé","referenciaMalha":"ORIGEM-2026"}
          }]
        }
        """;
        Assert.DoesNotThrow(() => validator.ParseAndValidate(json, "pessoas.jsonl", 1));
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
        throw new InvalidOperationException("Contrato de Pessoa não localizado a partir do diretório de teste.");
    }
}
