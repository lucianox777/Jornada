using System.Text.Json;
using Jornada.Processor.Worker;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class PersonIdentifierParsingTests
{
    private static string SyntheticCpfA() => string.Concat(Enumerable.Repeat("12", 5)) + "3";
    private static string SyntheticCpfB() => new('7', 11);

    [Test]
    public void Allows_Zero_Identifiers()
    {
        using var document = JsonDocument.Parse("{\"nomeCompleto\":\"Pessoa Sintetica\",\"dataNascimento\":\"1990-01-01\"}");

        var identifiers = PersonIdentifierParsing.Parse(document.RootElement, null, null, null);

        Assert.That(identifiers, Is.Empty);
    }

    [Test]
    public void Coalesces_Legacy_Cpf_With_Equivalent_Explicit_Cpf()
    {
        var cpfValue = SyntheticCpfA();
        using var document = JsonDocument.Parse($$"""
            {
              "identificadores":[
                {"tipo":"CPF","namespace":"BR","valor":"{{cpfValue}}","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        var identifiers = PersonIdentifierParsing.Parse(document.RootElement, cpfValue, null, null);

        Assert.That(identifiers, Has.Count.EqualTo(1));
        var cpf = identifiers.Single();
        Assert.Multiple(() =>
        {
            Assert.That(cpf.Tipo, Is.EqualTo("CPF"));
            Assert.That(cpf.Namespace, Is.EqualTo("BR"));
            Assert.That(cpf.ValorNormalizado, Is.EqualTo(cpfValue));
        });
    }

    [Test]
    public void Rejects_Legacy_Cpf_Divergent_From_Explicit_Cpf()
    {
        var legacyCpf = SyntheticCpfA();
        var explicitCpf = SyntheticCpfB();
        using var document = JsonDocument.Parse($$"""
            {
              "identificadores":[
                {"tipo":"CPF","namespace":"BR","valor":"{{explicitCpf}}","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        var error = Assert.Throws<InvalidDataException>(() =>
            PersonIdentifierParsing.Parse(document.RootElement, legacyCpf, null, null));

        Assert.That(error!.Message, Does.Contain("diverge").IgnoreCase);
    }

    [Test]
    public void Rejects_Two_Distinct_Explicit_Cpfs()
    {
        var cpfA = SyntheticCpfA();
        var cpfB = SyntheticCpfB();
        using var document = JsonDocument.Parse($$"""
            {
              "identificadores":[
                {"tipo":"CPF","namespace":"BR","valor":"{{cpfA}}","statusEvidencia":"DECLARADO"},
                {"tipo":"CPF","namespace":"BR","valor":"{{cpfB}}","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        var error = Assert.Throws<InvalidDataException>(() =>
            PersonIdentifierParsing.Parse(document.RootElement, null, null, null));

        Assert.That(error!.Message, Does.Contain("CPFs distintos").IgnoreCase);
    }

    [Test]
    public void Legacy_Source_Code_Does_Not_Fall_Back_To_Cpf()
    {
        using var document = JsonDocument.Parse("{}");

        var identifiers = PersonIdentifierParsing.Parse(document.RootElement, SyntheticCpfA(), null, "CADASTRO_SMADS");

        Assert.Multiple(() =>
        {
            Assert.That(identifiers, Has.Count.EqualTo(1));
            Assert.That(identifiers.Any(i => i.Tipo == "CODIGO_BASE_ORIGEM"), Is.False);
        });
    }

    [Test]
    public void Rejects_SourceIdentifier_From_Base_Different_From_Manifest()
    {
        using var document = JsonDocument.Parse("""
            {
              "identificadores":[
                {"tipo":"CODIGO_BASE_ORIGEM","namespace":"BASE_B","valor":"P-SYNTH-1","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        var error = Assert.Throws<InvalidDataException>(() =>
            PersonIdentifierParsing.Parse(document.RootElement, null, null, "BASE_A"));

        Assert.That(error!.Message, Does.Contain("diverge").IgnoreCase);
    }

    [Test]
    public void Rejects_Two_Distinct_Source_Codes_In_Same_Observation()
    {
        using var document = JsonDocument.Parse("""
            {
              "identificadores":[
                {"tipo":"CODIGO_BASE_ORIGEM","namespace":"BASE_A","valor":"P-SYNTH-1","statusEvidencia":"DECLARADO"},
                {"tipo":"CODIGO_BASE_ORIGEM","namespace":"BASE_A","valor":"P-SYNTH-2","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        var error = Assert.Throws<InvalidDataException>(() =>
            PersonIdentifierParsing.Parse(document.RootElement, null, null, "BASE_A"));

        Assert.That(error!.Message, Does.Contain("mais de um CODIGO_BASE_ORIGEM").IgnoreCase);
    }

    [Test]
    public void Explicit_Source_Code_With_Legacy_Code_Requires_Manifest_Base()
    {
        using var document = JsonDocument.Parse("""
            {
              "identificadores":[
                {"tipo":"CODIGO_BASE_ORIGEM","namespace":"BASE_A","valor":"P-SYNTH-1","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        var error = Assert.Throws<InvalidDataException>(() =>
            PersonIdentifierParsing.Parse(document.RootElement, null, "P-SYNTH-1", null));

        Assert.That(error!.Message, Does.Contain("exige codigoBasePessoaOrigem").IgnoreCase);
    }

    [Test]
    public void Requires_Rg_Issuer_And_State()
    {
        using var document = JsonDocument.Parse("""
            {
              "identificadores":[
                {"tipo":"RG","namespace":"TEST","valor":"RG-SYNTH","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        Assert.Throws<InvalidDataException>(() =>
            PersonIdentifierParsing.Parse(document.RootElement, null, null, null));
    }

    [Test]
    public void Normalizes_Jornada_Uuid_Without_Creating_External_Priority()
    {
        var uuid = Guid.NewGuid();
        using var document = JsonDocument.Parse($$"""
            {
              "identificadores":[
                {"tipo":"UUID_JORNADA","namespace":"JORNADA","valor":"{{uuid:B}}","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        var identifiers = PersonIdentifierParsing.Parse(document.RootElement, null, null, null);
        Assert.That(identifiers, Has.Count.EqualTo(1));
        var identifier = identifiers.Single();

        Assert.Multiple(() =>
        {
            Assert.That(identifier.Tipo, Is.EqualTo("UUID_JORNADA"));
            Assert.That(identifier.ValorNormalizado, Is.EqualTo(uuid.ToString("D")));
        });
    }
}
