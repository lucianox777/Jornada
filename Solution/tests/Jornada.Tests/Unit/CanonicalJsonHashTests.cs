using System.Text.Json;
using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class CanonicalJsonHashTests
{
    [Test]
    public void Canonical_hash_ignores_property_order_and_external_control_fields()
    {
        using var a = JsonDocument.Parse("""{"codigoRegistroOrigem":"AA-1","operacao":"INCLUSAO","valorConcedido":600,"situacaoVigencia":"VIGENTE"}""");
        using var b = JsonDocument.Parse("""{"situacaoVigencia":"VIGENTE","valorConcedido":600.0,"operacao":"RETIFICACAO","codigoRegistroOrigem":"AA-1"}""");
        Assert.That(CanonicalJsonHash.Compute(a.RootElement, "codigoRegistroOrigem", "operacao"),
            Is.EqualTo(CanonicalJsonHash.Compute(b.RootElement, "codigoRegistroOrigem", "operacao")));
    }

    [TestCase("600", "600.0")]
    [TestCase("600.00", "6e2")]
    public void Equivalent_numeric_representations_have_same_hash(string left, string right)
    {
        using var a = JsonDocument.Parse($"{{\"valor\":{left}}}");
        using var b = JsonDocument.Parse($"{{\"valor\":{right}}}");
        Assert.That(CanonicalJsonHash.Compute(a.RootElement), Is.EqualTo(CanonicalJsonHash.Compute(b.RootElement)));
    }

    [Test]
    public void Optional_null_and_absent_property_have_same_hash()
    {
        using var a = JsonDocument.Parse("""{"cpfAusenteMotivo":null,"nome":"Maria"}""");
        using var b = JsonDocument.Parse("""{"nome":"Maria"}""");
        Assert.That(CanonicalJsonHash.Compute(a.RootElement), Is.EqualTo(CanonicalJsonHash.Compute(b.RootElement)));
    }

    [Test]
    public void Unicode_nfc_and_nfd_have_same_hash()
    {
        using var a = JsonDocument.Parse("""{"nome":"José"}""");
        using var b = JsonDocument.Parse("""{"nome":"Jose\u0301"}""");
        Assert.That(CanonicalJsonHash.Compute(a.RootElement), Is.EqualTo(CanonicalJsonHash.Compute(b.RootElement)));
    }

    [Test]
    public void Person_set_arrays_are_order_independent_and_source_freshness_is_not_a_business_version()
    {
        using var a = JsonDocument.Parse(
            """
            {
              "codigoPessoaOrigem": "P-1",
              "nomeCompleto": "Maria",
              "dataNascimento": "1980-01-01",
              "nomeMae": "Ana",
              "atributosTransversais": [
                {
                  "atributoCodigo": "EMAIL",
                  "valor": "a@x.test",
                  "statusEvidencia": "DECLARADO",
                  "atualizadoEmOrigem": "2026-08-01T00:00:00Z"
                },
                {
                  "atributoCodigo": "TELEFONE",
                  "valor": "11999999999",
                  "statusEvidencia": "DECLARADO"
                }
              ]
            }
            """);

        using var b = JsonDocument.Parse(
            """
            {
              "nomeMae": "Ana",
              "dataNascimento": "1980-01-01",
              "nomeCompleto": "Maria",
              "codigoPessoaOrigem": "P-1",
              "atributosTransversais": [
                {
                  "atributoCodigo": "TELEFONE",
                  "valor": "11999999999",
                  "statusEvidencia": "DECLARADO"
                },
                {
                  "atualizadoEmOrigem": "2026-08-29T23:00:00-03:00",
                  "statusEvidencia": "DECLARADO",
                  "valor": "a@x.test",
                  "atributoCodigo": "EMAIL"
                }
              ]
            }
            """);

        Assert.That(
            CanonicalJsonHash.ComputePerson(a.RootElement),
            Is.EqualTo(CanonicalJsonHash.ComputePerson(b.RootElement)));
    }

    [Test]
    public void Evidence_semantics_still_change_person_hash()
    {
        using var a = JsonDocument.Parse("""{"codigoPessoaOrigem":"P-1","nomeCompleto":"Maria","dataNascimento":"1980-01-01","nomeMae":"Ana","atributosTransversais":[{"atributoCodigo":"EMAIL","valor":"a@x.test","statusEvidencia":"DECLARADO"}]}""");
        using var b = JsonDocument.Parse("""{"codigoPessoaOrigem":"P-1","nomeCompleto":"Maria","dataNascimento":"1980-01-01","nomeMae":"Ana","atributosTransversais":[{"atributoCodigo":"EMAIL","valor":"a@x.test","statusEvidencia":"COMPROVADO","verificadoEm":"2026-08-29T09:00:00-03:00"}]}""");
        Assert.That(CanonicalJsonHash.ComputePerson(a.RootElement), Is.Not.EqualTo(CanonicalJsonHash.ComputePerson(b.RootElement)));
    }

    [Test]
    public void Canonical_hash_changes_when_business_content_changes()
    {
        using var a = JsonDocument.Parse("""{"codigoRegistroOrigem":"AA-1","operacao":"INCLUSAO","valorConcedido":600}""");
        using var b = JsonDocument.Parse("""{"codigoRegistroOrigem":"AA-1","operacao":"RETIFICACAO","valorConcedido":1000}""");
        Assert.That(CanonicalJsonHash.Compute(a.RootElement, "codigoRegistroOrigem", "operacao"),
            Is.Not.EqualTo(CanonicalJsonHash.Compute(b.RootElement, "codigoRegistroOrigem", "operacao")));
    }

    [Test]
    public void Person_hash_does_not_turn_a_new_source_transaction_into_a_new_business_version()
    {
        using var a = JsonDocument.Parse("""{"codigoPessoaOrigem":"P-1","sourceTransactionId":"TX-1","nomeCompleto":"Maria","dataNascimento":"1980-01-01","nomeMae":"Ana"}""");
        using var b = JsonDocument.Parse("""{"sourceTransactionId":"TX-2","dataNascimento":"1980-01-01","nomeCompleto":"Maria","codigoPessoaOrigem":"P-1","nomeMae":"Ana"}""");
        Assert.That(CanonicalJsonHash.ComputePerson(a.RootElement), Is.EqualTo(CanonicalJsonHash.ComputePerson(b.RootElement)));
    }
}
