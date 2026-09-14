using System.Text.Json;
using Jornada.Processor.Worker;
using Xunit;

namespace Jornada.Tests.Unit;

public sealed class PersonIdentifierParsingTests
{
    [Fact]
    public void Allows_Zero_Identifiers()
    {
        using var document = JsonDocument.Parse("{\"nomeCompleto\":\"Pessoa\",\"dataNascimento\":\"1990-01-01\"}");

        var identifiers = PersonIdentifierParsing.Parse(document.RootElement, null, null, null);

        Assert.Empty(identifiers);
    }

    [Fact]
    public void Coalesces_Legacy_Cpf_With_Equivalent_Explicit_Cpf()
    {
        using var document = JsonDocument.Parse("""
            {
              "identificadores":[
                {"tipo":"CPF","namespace":"BR","valor":"12345678901","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        var identifiers = PersonIdentifierParsing.Parse(document.RootElement, "12345678901", null, null);

        var cpf = Assert.Single(identifiers);
        Assert.Equal("CPF", cpf.Tipo);
        Assert.Equal("BR", cpf.Namespace);
        Assert.Equal("12345678901", cpf.ValorNormalizado);
    }

    [Fact]
    public void Rejects_Legacy_Cpf_Divergent_From_Explicit_Cpf()
    {
        using var document = JsonDocument.Parse("""
            {
              "identificadores":[
                {"tipo":"CPF","namespace":"BR","valor":"10987654321","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        var error = Assert.Throws<InvalidDataException>(() =>
            PersonIdentifierParsing.Parse(document.RootElement, "12345678901", null, null));

        Assert.Contains("diverge", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_Two_Distinct_Explicit_Cpfs()
    {
        using var document = JsonDocument.Parse("""
            {
              "identificadores":[
                {"tipo":"CPF","namespace":"BR","valor":"11144477735","statusEvidencia":"DECLARADO"},
                {"tipo":"CPF","namespace":"BR","valor":"12345678901","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        var error = Assert.Throws<InvalidDataException>(() =>
            PersonIdentifierParsing.Parse(document.RootElement, null, null, null));

        Assert.Contains("CPFs distintos", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Legacy_Source_Code_Does_Not_Fall_Back_To_Cpf()
    {
        using var document = JsonDocument.Parse("{}");

        var identifiers = PersonIdentifierParsing.Parse(document.RootElement, "12345678901", null, "CADASTRO_SMADS");

        Assert.Single(identifiers);
        Assert.DoesNotContain(identifiers, i => i.Tipo == "CODIGO_BASE_ORIGEM");
    }

    [Fact]
    public void Requires_Rg_Issuer_And_State()
    {
        using var document = JsonDocument.Parse("""
            {
              "identificadores":[
                {"tipo":"RG","namespace":"SSP-SP","valor":"12.345.678-9","statusEvidencia":"DECLARADO"}
              ]
            }
            """);

        Assert.Throws<InvalidDataException>(() =>
            PersonIdentifierParsing.Parse(document.RootElement, null, null, null));
    }

    [Fact]
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

        var identifier = Assert.Single(PersonIdentifierParsing.Parse(document.RootElement, null, null, null));

        Assert.Equal("UUID_JORNADA", identifier.Tipo);
        Assert.Equal(uuid.ToString("D"), identifier.ValorNormalizado);
    }
}
