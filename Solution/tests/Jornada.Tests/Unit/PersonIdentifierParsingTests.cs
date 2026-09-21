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

    [TestCase("NIS")]
    [TestCase("PIS")]
    [TestCase("PASEP")]
    [TestCase("NIT")]
    public void Normalizes_Nis_And_Preserves_Allowed_Source_Namespace(string ns)
    {
        var json = """
            {
              "identificadores":[
                {"tipo":"NIS","namespace":"__NS__","valor":"120.00000.00-4","statusEvidencia":"COMPROVADO","verificadoEm":"2026-09-21T08:00:00-03:00"}
              ]
            }
            """.Replace("__NS__", ns, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);

        var identifier = PersonIdentifierParsing.Parse(document.RootElement, null, null, null).Single();

        Assert.Multiple(() =>
        {
            Assert.That(identifier.Tipo, Is.EqualTo("NIS"));
            Assert.That(identifier.Namespace, Is.EqualTo(ns));
            Assert.That(identifier.ValorNormalizado, Is.EqualTo("12000000004"));
            Assert.That(identifier.StatusEvidencia, Is.EqualTo("COMPROVADO"));
        });
    }

    [Test]
    public void Rejects_Nis_With_Unsupported_Namespace()
    {
        using var document = JsonDocument.Parse("""
            {
              "identificadores":[
                {"tipo":"NIS","namespace":"OUTRO","valor":"12000000004","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        var error = Assert.Throws<InvalidDataException>(() =>
            PersonIdentifierParsing.Parse(document.RootElement, null, null, null));

        Assert.That(error!.Message, Does.Contain("namespace NIS, PIS, PASEP ou NIT"));
    }

    [Test]
    public void Allows_Rg_Number_Only_And_Preserves_Leading_Zeros()
    {
        using var document = JsonDocument.Parse("""
            {
              "identificadores":[
                {"tipo":"RG","namespace":"BR-SP","valor":"001234567","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        var identifier = PersonIdentifierParsing.Parse(document.RootElement, null, null, null).Single();

        Assert.Multiple(() =>
        {
            Assert.That(identifier.Tipo, Is.EqualTo("RG"));
            Assert.That(identifier.ValorNormalizado, Is.EqualTo("001234567"));
            Assert.That(identifier.Emissor, Is.Null);
            Assert.That(identifier.UfEmissor, Is.Null);
        });
    }

    [Test]
    public void Allows_Rg_With_State_And_No_Issuer()
    {
        using var document = JsonDocument.Parse("""
            {
              "identificadores":[
                {"tipo":"RG","namespace":"BR-SP","valor":"12.345.678-X","ufEmissor":"SP","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        var identifier = PersonIdentifierParsing.Parse(document.RootElement, null, null, null).Single();

        Assert.Multiple(() =>
        {
            Assert.That(identifier.ValorNormalizado, Is.EqualTo("12345678X"));
            Assert.That(identifier.Emissor, Is.Null);
            Assert.That(identifier.UfEmissor, Is.EqualTo("SP"));
        });
    }

    [Test]
    public void Allows_Rg_With_Issuer_And_No_State()
    {
        using var document = JsonDocument.Parse("""
            {
              "identificadores":[
                {"tipo":"RG","namespace":"BR","valor":"12.345.678-9","emissor":"SSP","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        var identifier = PersonIdentifierParsing.Parse(document.RootElement, null, null, null).Single();

        Assert.Multiple(() =>
        {
            Assert.That(identifier.ValorNormalizado, Is.EqualTo("123456789"));
            Assert.That(identifier.Emissor, Is.EqualTo("SSP"));
            Assert.That(identifier.UfEmissor, Is.Null);
        });
    }

    [Test]
    public void Normalizes_Rg_Punctuation_And_Preserves_X()
    {
        using var document = JsonDocument.Parse("""
            {
              "identificadores":[
                {"tipo":"RG","namespace":"BR-SP","valor":"00.123.456-X","emissor":"SSP","ufEmissor":"sp","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        var identifier = PersonIdentifierParsing.Parse(document.RootElement, null, null, null).Single();

        Assert.Multiple(() =>
        {
            Assert.That(identifier.ValorOriginal, Is.EqualTo("00.123.456-X"));
            Assert.That(identifier.ValorNormalizado, Is.EqualTo("00123456X"));
            Assert.That(identifier.UfEmissor, Is.EqualTo("SP"));
        });
    }

    [Test]
    public void Accepts_Cnh_As_Secondary_Identifier_Without_Structural_Validation()
    {
        using var document = JsonDocument.Parse("""
            {
              "identificadores":[
                {"tipo":"CNH","namespace":"BR","valor":"001.234.567-89","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        var identifier = PersonIdentifierParsing.Parse(document.RootElement, null, null, null).Single();

        Assert.Multiple(() =>
        {
            Assert.That(identifier.Tipo, Is.EqualTo("CNH"));
            Assert.That(identifier.Namespace, Is.EqualTo("BR"));
            Assert.That(identifier.ValorOriginal, Is.EqualTo("001.234.567-89"));
            Assert.That(identifier.ValorNormalizado, Is.EqualTo("00123456789"));
            Assert.That(identifier.StatusEvidencia, Is.EqualTo("DECLARADO"));
        });
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
