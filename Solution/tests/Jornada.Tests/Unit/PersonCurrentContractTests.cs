using System.Security.Cryptography;
using System.Text.Json;
using Jornada.Ingestion;
using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class PersonCurrentContractTests
{
    private static readonly string[] Gestores = ["SEHAB", "SMADS", "SMDET", "SMS"];

    [TestCase("NAO_INFORMADO_ORIGEM")]
    [TestCase("SEM_DOCUMENTACAO_BASE_DECLARADA")]
    [TestCase("COM_DOCUMENTACAO_SEM_CPF_CONHECIDO")]
    [TestCase("EM_REGULARIZACAO")]
    public void Current_contract_accepts_explicit_cpf_absence_taxonomy(string reason)
    {
        Assert.DoesNotThrow(() => PersonContractRules.ValidateCpfAbsence(null, reason));
        Assert.That(PersonContractRules.IsCpfAbsenceReason(reason), Is.True);
    }

    [Test]
    public void Current_contract_rejects_legacy_or_inconsistent_cpf_absence()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidDataException>(() =>
                PersonContractRules.ValidateCpfAbsence(null, "SEM_CPF"));
            Assert.Throws<InvalidDataException>(() =>
                PersonContractRules.ValidateCpfAbsence(null, null));
            Assert.Throws<InvalidDataException>(() =>
                PersonContractRules.ValidateCpfAbsence("11144477735", "NAO_INFORMADO_ORIGEM"));
            Assert.DoesNotThrow(() =>
                PersonContractRules.ValidateCpfAbsence("11144477735", null));
        });
    }

    [TestCaseSource(nameof(Gestores))]
    public void Current_v6_requires_only_delivery_key_and_keeps_identity_core_optional(string gestor)
    {
        using var schema = LoadV6(gestor);
        var required = schema.RootElement.GetProperty("required")
            .EnumerateArray().Select(x => x.GetString()).ToArray();
        var properties = schema.RootElement.GetProperty("properties");

        Assert.Multiple(() =>
        {
            Assert.That(required, Is.EqualTo(new[] { "idPessoaEntrega" }));
            Assert.That(Types(properties, "cpf"), Does.Contain("null"));
            Assert.That(Types(properties, "nomeCompleto"), Does.Contain("null"));
            Assert.That(Types(properties, "dataNascimento"), Does.Contain("null"));
            Assert.That(Types(properties, "nomeMae"), Does.Contain("null"));
        });
    }

    [Test]
    public void Current_v6_accepts_partial_identifiers_and_typed_territorial_reference()
    {
        var validator = LoadV6Validator("SMADS");
        const string json = """
        {
          "idPessoaEntrega":"P-V6-1",
          "cpf":null,
          "cpfAusenteMotivo":"NAO_INFORMADO_ORIGEM",
          "nomeCompleto":"Pessoa Sintetica",
          "dataNascimento":null,
          "nomeMae":null,
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
    public void Current_v6_rejects_legacy_cpf_bucket_and_legacy_territorial_nature()
    {
        var validator = LoadV6Validator("SMADS");
        const string oldCpf = """
        {
          "idPessoaEntrega":"P-V6-2","cpf":null,"cpfAusenteMotivo":"SEM_CPF",
          "nomeCompleto":null,"dataNascimento":null,"nomeMae":null
        }
        """;
        const string oldTerritorialNature = """
        {
          "idPessoaEntrega":"P-V6-3","cpf":null,"cpfAusenteMotivo":"NAO_INFORMADO_ORIGEM",
          "nomeCompleto":null,"dataNascimento":null,"nomeMae":null,
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
    public void Current_contract_inventory_contains_only_pessoa_v6()
    {
        var root = FindRepositoryRoot();
        using var governance = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "Solution", "config", "governance", "schema-approvals.json")));

        var personPaths = governance.RootElement.GetProperty("contracts").EnumerateArray()
            .Select(x => x.GetProperty("path").GetString())
            .Where(x => x is not null && x.Contains("/pessoa/", StringComparison.Ordinal))
            .ToArray();

        Assert.That(personPaths, Is.Not.Empty);
        Assert.That(personPaths, Has.All.Contains("/pessoa/v6/"));
    }

    [Test]
    public void Current_v6_hashes_match_governance_inventory()
    {
        var root = FindRepositoryRoot();
        using var governance = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "Solution", "config", "governance", "schema-approvals.json")));

        foreach (var gestor in new[] { "SMADS", "SMDET", "SMS" })
        {
            var relative = $"config/contracts/gestores/{gestor}/pessoa/v6/pessoa.schema.json";
            var expected = governance.RootElement.GetProperty("contracts").EnumerateArray()
                .Single(x => x.GetProperty("path").GetString() == relative)
                .GetProperty("sha256").GetString();

            var bytes = File.ReadAllBytes(Path.Combine(root, "Solution", relative.Replace('/', Path.DirectorySeparatorChar)));
            var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Assert.That(actual, Is.EqualTo(expected), $"{gestor} v6 hash divergiu do inventário.");
        }
    }

    [Test]
    public void Rg_and_cnh_normalization_rejects_unknown_punctuation()
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

    private static string[] Types(JsonElement properties, string property)
    {
        var type = properties.GetProperty(property).GetProperty("type");
        return type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(x => x.GetString()!).ToArray()
            : [type.GetString()!];
    }

    private static JsonDocument LoadV6(string gestor)
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "Solution", "config", "contracts", "gestores", gestor, "pessoa", "v6", "pessoa.schema.json");
        Assert.That(File.Exists(path), Is.True, $"Contrato Pessoa v6 ausente para {gestor}: {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static JsonSchemaSubsetValidator LoadV6Validator(string gestor)
    {
        var root = FindRepositoryRoot();
        return JsonSchemaSubsetValidator.Load(Path.Combine(
            root, "Solution", "config", "contracts", "gestores", gestor, "pessoa", "v6", "pessoa.schema.json"));
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
