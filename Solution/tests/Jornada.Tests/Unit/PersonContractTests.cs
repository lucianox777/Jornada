using System.Security.Cryptography;
using System.Text.Json;
using Jornada.Ingestion;
using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class PersonContractTests
{
    [TestCase("NAO_INFORMADO_ORIGEM")]
    [TestCase("SEM_DOCUMENTACAO_BASE_DECLARADA")]
    [TestCase("COM_DOCUMENTACAO_SEM_CPF_CONHECIDO")]
    [TestCase("EM_REGULARIZACAO")]
    public void Current_contract_accepts_only_explicit_cpf_absence_taxonomy(string reason)
    {
        Assert.DoesNotThrow(() =>
            PersonContractRules.ValidateCpfAbsence(PersonContractRules.CurrentSchemaVersion, null, reason));
        Assert.That(PersonContractRules.IsCpfAbsenceReason(reason), Is.True);
    }

    [Test]
    public void Current_contract_rejects_invalid_cpf_absence_states()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidDataException>(() =>
                PersonContractRules.ValidateCpfAbsence(PersonContractRules.CurrentSchemaVersion, null, "SEM_CPF"));
            Assert.Throws<InvalidDataException>(() =>
                PersonContractRules.ValidateCpfAbsence(PersonContractRules.CurrentSchemaVersion, null, null));
            Assert.Throws<InvalidDataException>(() =>
                PersonContractRules.ValidateCpfAbsence(PersonContractRules.CurrentSchemaVersion, "11144477735", "NAO_INFORMADO_ORIGEM"));
            Assert.DoesNotThrow(() =>
                PersonContractRules.ValidateCpfAbsence(PersonContractRules.CurrentSchemaVersion, "11144477735", null));
            Assert.Throws<InvalidDataException>(() =>
                PersonContractRules.ValidateCpfAbsence(PersonContractRules.CurrentSchemaVersion - 1, null, "NAO_INFORMADO_ORIGEM"));
        });
    }

    [Test]
    public void Current_schema_accepts_partial_rg_cnh_and_typed_informed_reference()
    {
        var validator = LoadCurrentValidator();
        const string json = """
        {
          "idPessoaEntrega":"P-CURRENT-1",
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
    public void Current_schema_accepts_declared_no_fixed_address_without_fine_address()
    {
        var validator = LoadCurrentValidator();
        const string json = """
        {
          "idPessoaEntrega":"P-CURRENT-2",
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
    public void Current_schema_rejects_unsupported_cpf_bucket_and_untyped_territorial_nature()
    {
        var validator = LoadCurrentValidator();
        const string invalidCpf = """
        {
          "idPessoaEntrega":"P-CURRENT-3","cpf":null,"cpfAusenteMotivo":"SEM_CPF",
          "nomeCompleto":"Pessoa Sintetica","dataNascimento":"1990-01-01"
        }
        """;
        const string invalidTerritorialNature = """
        {
          "idPessoaEntrega":"P-CURRENT-4","cpf":null,"cpfAusenteMotivo":"NAO_INFORMADO_ORIGEM",
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
            Assert.That(() => validator.ParseAndValidate(invalidCpf, "pessoas.jsonl", 1),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => validator.ParseAndValidate(invalidTerritorialNature, "pessoas.jsonl", 2),
                Throws.TypeOf<InvalidDataException>());
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
    public void Current_schema_hashes_match_governance_inventory_and_only_v1_is_kept()
    {
        var root = FindRepositoryRoot();
        using var governance = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "Solution", "config", "governance", "schema-approvals.json")));

        foreach (var gestor in new[] { "SEHAB", "SMADS", "SMDET", "SMS" })
        {
            var relative = $"config/contracts/gestores/{gestor}/pessoa/v{PersonContractRules.CurrentSchemaVersion}/pessoa.schema.json";
            var expected = governance.RootElement.GetProperty("contracts").EnumerateArray()
                .Single(x => x.GetProperty("path").GetString() == relative)
                .GetProperty("sha256").GetString();

            var path = Path.Combine(root, "Solution", relative.Replace('/', Path.DirectorySeparatorChar));
            var bytes = File.ReadAllBytes(path);
            var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            using var schema = JsonDocument.Parse(bytes);
            var required = schema.RootElement.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.EqualTo(expected), $"{gestor} contrato corrente divergiu do inventário.");
                Assert.That(required, Is.EqualTo(new[] { "idPessoaEntrega" }), $"{gestor} não deve exigir campos do núcleo.");
                for (var oldVersion = 2; oldVersion <= 6; oldVersion++)
                {
                    var oldPath = Path.Combine(root, "Solution", "config", "contracts", "gestores", gestor, "pessoa", $"v{oldVersion}", "pessoa.schema.json");
                    Assert.That(File.Exists(oldPath), Is.False, $"{gestor} ainda mantém contrato Pessoa antigo v{oldVersion}.");
                }
            });
        }
    }

    private static JsonSchemaSubsetValidator LoadCurrentValidator()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "config", "contracts", "gestores", "SMADS", "pessoa",
            $"v{PersonContractRules.CurrentSchemaVersion}", "pessoa.schema.json");
        Assert.That(File.Exists(path), Is.True, "Contrato Pessoa corrente deve ser copiado para a fixture de testes.");
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
