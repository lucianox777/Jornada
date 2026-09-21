using System.Security.Cryptography;
using System.Text.Json;
using Jornada.Ingestion;
using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class PersonV5ContractTests
{
    [TestCase("NAO_INFORMADO_ORIGEM")]
    [TestCase("SEM_DOCUMENTACAO_BASE_DECLARADA")]
    [TestCase("COM_DOCUMENTACAO_SEM_CPF_CONHECIDO")]
    [TestCase("EM_REGULARIZACAO")]
    public void Cpf_v5_accepts_only_explicit_absence_taxonomy(string reason)
    {
        Assert.DoesNotThrow(() => PersonV5ContractRules.ValidateCpfAbsence(5, null, reason));
        Assert.That(PersonV5ContractRules.IsCpfAbsenceReasonV5(reason), Is.True);
    }

    [Test]
    public void Cpf_v5_rejects_legacy_sem_cpf_missing_reason_and_reason_with_cpf()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidDataException>(() =>
                PersonV5ContractRules.ValidateCpfAbsence(5, null, "SEM_CPF"));
            Assert.Throws<InvalidDataException>(() =>
                PersonV5ContractRules.ValidateCpfAbsence(5, null, null));
            Assert.Throws<InvalidDataException>(() =>
                PersonV5ContractRules.ValidateCpfAbsence(5, "11144477735", "NAO_INFORMADO_ORIGEM"));
            Assert.DoesNotThrow(() =>
                PersonV5ContractRules.ValidateCpfAbsence(5, "11144477735", null));
        });
    }

    [Test]
    public void Cpf_v4_keeps_historical_semantics_without_v5_reinterpretation()
    {
        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() =>
                PersonV5ContractRules.ValidateCpfAbsence(4, null, "SEM_CPF"));
            Assert.DoesNotThrow(() =>
                PersonV5ContractRules.ValidateCpfAbsence(4, null, null));
        });
    }

    [Test]
    public void Pessoa_v5_schema_accepts_partial_rg_cnh_and_typed_informed_reference()
    {
        var validator = LoadV5Validator();
        const string json = """
        {
          "idPessoaEntrega":"P-V5-1",
          "cpf":null,
          "cpfAusenteMotivo":"NAO_INFORMADO_ORIGEM",
          "nomeCompleto":"Pessoa Sintetica",
          "dataNascimento":"1990-01-01",
          "identificadores":[
            {"tipo":"RG","namespace":"BR-SP","valor":"00123456X","statusEvidencia":"DECLARADO"},
            {"tipo":"CNH","namespace":"BR","valor":"00123456789","statusEvidencia":"DECLARADO"}
          ],
          "atributosTransversais":[{
            "atributoCodigo":"REFERENCIA_TERRITORIAL",
            "estadoReferenciaTerritorial":"INFORMADA",
            "naturezaReferenciaTerritorial":"SERVICO_REFERENCIA",
            "valor":"CRAS SINTETICO",
            "statusEvidencia":"DECLARADO",
            "situacaoGeografia":"NAO_RESOLVIDA_ORIGEM"
          }]
        }
        """;

        Assert.DoesNotThrow(() => validator.ParseAndValidate(json, "pessoas.jsonl", 1));
    }

    [Test]
    public void Pessoa_v5_schema_accepts_declared_no_fixed_address_without_fine_address()
    {
        var validator = LoadV5Validator();
        const string json = """
        {
          "idPessoaEntrega":"P-V5-2",
          "cpf":null,
          "cpfAusenteMotivo":"SEM_DOCUMENTACAO_BASE_DECLARADA",
          "nomeCompleto":"Pessoa Sintetica",
          "dataNascimento":"1990-01-01",
          "atributosTransversais":[{
            "atributoCodigo":"REFERENCIA_TERRITORIAL",
            "estadoReferenciaTerritorial":"SEM_ENDERECO_FIXO_DECLARADO",
            "statusEvidencia":"DECLARADO"
          }]
        }
        """;

        Assert.DoesNotThrow(() => validator.ParseAndValidate(json, "pessoas.jsonl", 1));
    }

    [Test]
    public void Pessoa_v5_schema_rejects_legacy_cpf_bucket_and_legacy_territorial_nature()
    {
        var validator = LoadV5Validator();
        const string oldCpf = """
        {
          "idPessoaEntrega":"P-V5-3","cpf":null,"cpfAusenteMotivo":"SEM_CPF",
          "nomeCompleto":"Pessoa Sintetica","dataNascimento":"1990-01-01"
        }
        """;
        const string oldTerritorialNature = """
        {
          "idPessoaEntrega":"P-V5-4","cpf":null,"cpfAusenteMotivo":"NAO_INFORMADO_ORIGEM",
          "nomeCompleto":"Pessoa Sintetica","dataNascimento":"1990-01-01",
          "atributosTransversais":[{
            "atributoCodigo":"REFERENCIA_TERRITORIAL",
            "estadoReferenciaTerritorial":"INFORMADA",
            "naturezaReferenciaTerritorial":"REFERENCIA_TERRITORIAL_DECLARADA",
            "statusEvidencia":"DECLARADO",
            "situacaoGeografia":"NAO_RESOLVIDA_ORIGEM",
            "valor":"REFERENCIA"
          }]
        }
        """;

        Assert.Multiple(() =>
        {
            Assert.That(() => validator.ParseAndValidate(oldCpf, "pessoas.jsonl", 1),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => validator.ParseAndValidate(oldTerritorialNature, "pessoas.jsonl", 2),
                Throws.TypeOf<InvalidDataException>());
        });
    }

    [Test]
    public void Pessoa_v4_schema_remains_historical_and_does_not_gain_v5_contract()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "Solution", "config", "contracts", "gestores", "SEHAB", "pessoa", "v4", "pessoa.schema.json")));

        var properties = document.RootElement.GetProperty("properties");
        var reasons = properties.GetProperty("cpfAusenteMotivo").GetProperty("enum")
            .EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).ToArray();
        var identifierRules = properties.GetProperty("identificadores").GetProperty("items").GetProperty("allOf");
        var transversalProperties = properties.GetProperty("atributosTransversais")
            .GetProperty("items").GetProperty("properties");
        var rgStillRequiresQualification = identifierRules.EnumerateArray().Any(rule =>
            rule.TryGetProperty("if", out var condition)
            && condition.GetProperty("properties").GetProperty("tipo").GetProperty("const").GetString() == "RG"
            && rule.GetProperty("then").GetProperty("required").EnumerateArray()
                .Select(x => x.GetString()).SequenceEqual(new[] { "emissor", "ufEmissor" }));

        Assert.Multiple(() =>
        {
            Assert.That(reasons, Does.Contain("SEM_CPF"));
            Assert.That(transversalProperties.TryGetProperty("estadoReferenciaTerritorial", out _), Is.False);
            Assert.That(rgStillRequiresQualification, Is.True);
        });
    }

    [Test]
    public void Rg_and_cnh_normalization_rejects_unknown_punctuation_instead_of_silently_dropping_it()
    {
        using var rg = JsonDocument.Parse("""
        {
          "identificadores":[
            {"tipo":"RG","namespace":"BR-SP","valor":"12@345","statusEvidencia":"DECLARADO"}
          ]
        }
        """);
        using var cnh = JsonDocument.Parse("""
        {
          "identificadores":[
            {"tipo":"CNH","namespace":"BR","valor":"001#234","statusEvidencia":"DECLARADO"}
          ]
        }
        """);

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidDataException>(() =>
                PersonIdentifierParsing.Parse(rg.RootElement, null, null, null));
            Assert.Throws<InvalidDataException>(() =>
                PersonIdentifierParsing.Parse(cnh.RootElement, null, null, null));
        });
    }

    [Test]
    public void Pessoa_v5_migration_preserves_secondary_identifiers_and_blocks_prison_from_shared_view()
    {
        var root = FindRepositoryRoot();
        var sql = File.ReadAllText(Path.Combine(
            root, "Solution", "database", "migrations", "20260921_Pessoa_V5_Contrato_371.sql"));

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("SEM_DOCUMENTACAO_BASE_DECLARADA"));
            Assert.That(sql, Does.Contain("COM_DOCUMENTACAO_SEM_CPF_CONHECIDO"));
            Assert.That(sql, Does.Contain("LEGADO_SEM_CPF_NAO_DECOMPOSTO"));
            Assert.That(sql, Does.Contain("tipo_identificador_codigo IN(N'NIS',N'RG',N'CNH')"));
            Assert.That(sql, Does.Contain("DROP CONSTRAINT ck_pessoa_identificador_rg"));
            Assert.That(sql, Does.Contain("estado_referencia"));
            Assert.That(sql, Does.Contain("SEM_ENDERECO_FIXO_DECLARADO"));
            Assert.That(sql, Does.Contain("rt.natureza_referencia<>N'INSTITUCIONAL_PRISIONAL'"));
            Assert.That(sql, Does.Contain("THEN N'RESTRITA'"));
            Assert.That(sql, Does.Not.Contain("INSERT identidade.identity_map").IgnoreCase);
            Assert.That(sql, Does.Not.Contain("CNH_DETERMINIST").IgnoreCase);
        });
    }

    [Test]
    public void Pessoa_v5_schema_hashes_match_governance_inventory()
    {
        var root = FindRepositoryRoot();
        using var governance = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "Solution", "config", "governance", "schema-approvals.json")));

        foreach (var gestor in new[] { "SEHAB", "SMADS", "SMDET", "SMS" })
        {
            var relative = $"config/contracts/gestores/{gestor}/pessoa/v5/pessoa.schema.json";
            var expected = governance.RootElement.GetProperty("contracts").EnumerateArray()
                .Single(x => x.GetProperty("path").GetString() == relative)
                .GetProperty("sha256").GetString();

            var bytes = File.ReadAllBytes(Path.Combine(root, "Solution", relative.Replace('/', Path.DirectorySeparatorChar)));
            var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Assert.That(actual, Is.EqualTo(expected), $"{gestor} v5 hash divergiu do inventário.");
        }
    }

    private static JsonSchemaSubsetValidator LoadV5Validator()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "config", "contracts", "gestores", "SEHAB", "pessoa", "v5", "pessoa.schema.json");
        Assert.That(File.Exists(path), Is.True, "Contrato Pessoa v5 deve ser copiado para a fixture de testes.");
        return JsonSchemaSubsetValidator.Load(path);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(current.FullName, "Solution")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Raiz do repositório Jornada não encontrada.");
    }
}
