using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jornada.Ingestion;

namespace Jornada.Tests.Unit;

/// <summary>
/// Exercises the existing AA01/CRA1 schemas through the real product validator.
/// This fixture does not authorize ingestion or certify HTTP/SQL/business rules.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class M1Aa01Cra1RuntimeContractTests
{
    private static readonly IReadOnlyDictionary<string, string> SchemaHashes = new Dictionary<string, string>
    {
        ["AA01"] = "8fa7723753cb083a8be6dc1bf083f24dac41dddd55e731be00f113b4313e616d",
        ["CRA1"] = "c6342a5fc5486d1e180b2dd8bd9e5b2e02fc1c8562ed6c84968e1da872612154"
    };

    private static string SchemaPath(string contract) => Path.Combine(
        TestContext.CurrentContext.TestDirectory, "config", "contracts", "registros", contract, "v1", "registro.schema.json");

    private static JsonSchemaSubsetValidator Validator(string contract)
    {
        var path = SchemaPath(contract);
        Assert.That(File.Exists(path), Is.True, "The real versioned schema must be copied by the existing test project.");
        Assert.That(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
            Is.EqualTo(SchemaHashes[contract]), "Do not change a published v1 schema to make the fixture pass.");
        return new JsonSchemaValidatorCache().Get(path, Convert.FromHexString(SchemaHashes[contract]));
    }

    private static JsonObject Baseline(string contract) => JsonNode.Parse(contract == "AA01" ? """
        {"idPessoaEntrega":"M1-SYNTHETIC-1","codigoRegistroOrigem":"M1-FACT-1","operacao":"INCLUSAO","dataInicioConcessao":"2026-11-22","situacaoVigencia":"VIGENTE","valorConcedido":100.50}
        """ : """
        {"idPessoaEntrega":"M1-SYNTHETIC-1","codigoRegistroOrigem":"M1-FACT-1","operacao":"INCLUSAO","dataHoraServico":"2026-11-22T08:15:00-03:00"}
        """)!.AsObject();

    private static JsonObject Changed(string contract, string field, object? value)
    {
        var json = Baseline(contract);
        json[field] = JsonSerializer.SerializeToNode(value);
        return json;
    }

    private static TestCaseData Case(string contract, string name, JsonObject json, bool accepted,
        string errorPath = "", string errorRule = "") =>
        new TestCaseData(contract, json.ToJsonString(), accepted, errorPath, errorRule)
            .SetName($"M1_{contract}_{name}");

    public static IEnumerable<TestCaseData> FactualCases()
    {
        foreach (var contract in new[] { "AA01", "CRA1" })
        {
            yield return Case(contract, "minimal_valid", Baseline(contract), true);
            var required = contract == "AA01"
                ? new[] { "idPessoaEntrega", "codigoRegistroOrigem", "operacao", "dataInicioConcessao", "situacaoVigencia", "valorConcedido" }
                : new[] { "idPessoaEntrega", "codigoRegistroOrigem", "operacao", "dataHoraServico" };
            foreach (var field in required)
            {
                var absent = Baseline(contract);
                absent.Remove(field);
                yield return Case(contract, "missing_" + field, absent, false, "$." + field, "obrigatória");
            }
            foreach (var operation in new[] { "INCLUSAO", "ALTERACAO", "RETIFICACAO", "EXCLUSAO" })
                yield return Case(contract, "operation_" + operation, Changed(contract, "operacao", operation), true);
            yield return Case(contract, "operation_invalid", Changed(contract, "operacao", "UPSERT"), false, "$.operacao", "domínio");
            yield return Case(contract, "additional_property", Changed(contract, "unexpected", "synthetic"), false, "$.unexpected", "adicional");
            yield return Case(contract, "person_reference_null", Changed(contract, "idPessoaEntrega", null), false, "$.idPessoaEntrega", "tipo JSON");
            yield return Case(contract, "person_reference_number", Changed(contract, "idPessoaEntrega", 1), false, "$.idPessoaEntrega", "tipo JSON");
            yield return Case(contract, "person_reference_empty", Changed(contract, "idPessoaEntrega", ""), false, "$.idPessoaEntrega", "minLength");
            yield return Case(contract, "person_reference_max", Changed(contract, "idPessoaEntrega", new string('P', 120)), true);
            yield return Case(contract, "person_reference_too_long", Changed(contract, "idPessoaEntrega", new string('P', 121)), false, "$.idPessoaEntrega", "maxLength");
            yield return Case(contract, "record_code_empty", Changed(contract, "codigoRegistroOrigem", ""), false, "$.codigoRegistroOrigem", "minLength");
            yield return Case(contract, "record_code_max", Changed(contract, "codigoRegistroOrigem", new string('R', 255)), true);
            yield return Case(contract, "record_code_too_long", Changed(contract, "codigoRegistroOrigem", new string('R', 256)), false, "$.codigoRegistroOrigem", "maxLength");
        }

        foreach (var reason in new[] { "TERMINO_REGULAR", "CANCELAMENTO", "CESSACAO" })
        {
            var closed = Changed("AA01", "situacaoVigencia", "ENCERRADA");
            closed["motivoEncerramento"] = reason;
            yield return Case("AA01", "closed_" + reason, closed, true);
        }
        yield return Case("AA01", "closed_requires_reason", Changed("AA01", "situacaoVigencia", "ENCERRADA"), false, "$.motivoEncerramento", "obrigatória");
        var nullReason = Changed("AA01", "situacaoVigencia", "ENCERRADA");
        nullReason["motivoEncerramento"] = null;
        yield return Case("AA01", "closed_reason_not_null", nullReason, false, "$.motivoEncerramento", "tipo JSON");
        yield return Case("AA01", "active_reason_must_be_null", Changed("AA01", "motivoEncerramento", "CANCELAMENTO"), false, "$.motivoEncerramento", "tipo JSON");
        yield return Case("AA01", "active_null_reason", Changed("AA01", "motivoEncerramento", null), true);
        yield return Case("AA01", "suspended", Changed("AA01", "situacaoVigencia", "SUSPENSA"), true);
        yield return Case("AA01", "status_invalid", Changed("AA01", "situacaoVigencia", "ATIVA"), false, "$.situacaoVigencia", "domínio");
        yield return Case("AA01", "nullable_amount", Changed("AA01", "valorConcedido", null), true);
        yield return Case("AA01", "amount_is_not_string", Changed("AA01", "valorConcedido", "100.50"), false, "$.valorConcedido", "tipo JSON");
        yield return Case("AA01", "nullable_start_date", Changed("AA01", "dataInicioConcessao", null), true);
        yield return Case("AA01", "impossible_date", Changed("AA01", "dataInicioConcessao", "2026-02-30"), false, "$.dataInicioConcessao", "formato date");
        yield return Case("AA01", "date_not_datetime", Changed("AA01", "dataInicioConcessao", "2026-11-22T08:15:00Z"), false, "$.dataInicioConcessao", "formato date");

        yield return Case("CRA1", "utc_datetime", Changed("CRA1", "dataHoraServico", "2026-11-22T11:15:00Z"), true);
        yield return Case("CRA1", "fractional_datetime", Changed("CRA1", "dataHoraServico", "2026-11-22T11:15:00.1234567Z"), true);
        yield return Case("CRA1", "datetime_not_null", Changed("CRA1", "dataHoraServico", null), false, "$.dataHoraServico", "tipo JSON");
        yield return Case("CRA1", "impossible_datetime", Changed("CRA1", "dataHoraServico", "2026-02-30T08:15:00-03:00"), false, "$.dataHoraServico", "formato date-time");
        yield return Case("CRA1", "invalid_datetime", Changed("CRA1", "dataHoraServico", "not-a-date"), false, "$.dataHoraServico", "formato date-time");
        foreach (var (field, maximum) in new[] { ("unidadeServico", 200), ("situacao", 80) })
        {
            yield return Case("CRA1", field + "_null", Changed("CRA1", field, null), true);
            yield return Case("CRA1", field + "_maximum", Changed("CRA1", field, new string('S', maximum)), true);
            yield return Case("CRA1", field + "_too_long", Changed("CRA1", field, new string('S', maximum + 1)), false, "$." + field, "maxLength");
        }
    }

    [TestCaseSource(nameof(FactualCases))]
    public void Checks_pinned_factual_schema(string contract, string json, bool accepted, string errorPath, string errorRule)
    {
        var validator = Validator(contract);
        if (accepted)
        {
            Assert.DoesNotThrow((Action)(() => validator.ParseAndValidate(json, "registros.jsonl", 1)));
            return;
        }
        var error = Assert.Throws<InvalidDataException>((TestDelegate)(() => validator.ParseAndValidate(json, "registros.jsonl", 1)));
        Assert.That(error!.Message, Does.Contain(errorPath).And.Contain(errorRule),
            "A negative must fail at its declared field and rule, not for an unrelated reason.");
    }

    [TestCase("AA01")]
    [TestCase("CRA1")]
    public void Rejects_an_unapproved_schema_digest(string contract)
    {
        _ = Validator(contract);
        var wrongDigest = Convert.FromHexString(SchemaHashes[contract]);
        wrongDigest[0] ^= 1;
        Assert.Throws<InvalidOperationException>((TestDelegate)(() => new JsonSchemaValidatorCache().Get(SchemaPath(contract), wrongDigest)));
    }

    // Diagnostic observations only: a Passed test records the measurement, not format conformance.
    // Do not silently turn these observations into a new acceptance or compatibility policy.
    [TestCase("date_only", "2026-11-22")]
    [TestCase("missing_offset", "2026-11-22T08:15:00")]
    public void Reports_date_time_behavior_separately_from_conformance(string caseId, string value)
    {
        var validator = Validator("CRA1");
        Assert.DoesNotThrow((Action)(() => validator.ParseAndValidate(Baseline("CRA1").ToJsonString(), "registros.jsonl", 1)));
        Assert.Throws<InvalidDataException>((TestDelegate)(() => validator.ParseAndValidate(
            Changed("CRA1", "dataHoraServico", "not-a-date").ToJsonString(), "registros.jsonl", 1)));
        var actualAccepted = true;
        try { validator.ParseAndValidate(Changed("CRA1", "dataHoraServico", value).ToJsonString(), "registros.jsonl", 1); }
        catch (InvalidDataException) { actualAccepted = false; }
        TestContext.Out.WriteLine("M1_FORMAT_OBSERVATION=" + JsonSerializer.Serialize(new
        {
            schema = "M1_FORMAT_OBSERVATION_V1", contract = "CRA1/v1", caseId,
            expectedByReference = false, actualAccepted, conforms = !actualAccepted,
            scope = "SCHEMA_VALIDATOR_ONLY", httpSqlVerified = false, authorizesIngestion = false
        }));
    }
}
