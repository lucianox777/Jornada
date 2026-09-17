using System.Text.Json;
using Xunit;

namespace Jornada.Tests.Unit;

public sealed class PersonV4ContractTests
{
    public static IEnumerable<object[]> Gestores()
    {
        yield return new object[] { "SEHAB" };
        yield return new object[] { "SMADS" };
        yield return new object[] { "SMDET" };
        yield return new object[] { "SMS" };
    }

    [Theory]
    [MemberData(nameof(Gestores))]
    public void V4_Requires_Demographic_Core_But_Not_Identifier(string gestor)
    {
        using var schema = Load(gestor);
        var required = schema.RootElement.GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();

        Assert.Contains("nomeCompleto", required);
        Assert.Contains("dataNascimento", required);
        Assert.DoesNotContain("cpf", required);
        Assert.DoesNotContain("codigoPessoaOrigem", required);
        Assert.DoesNotContain("identificadores", required);
    }

    [Theory]
    [MemberData(nameof(Gestores))]
    public void V4_Exposes_Zero_To_Many_Typed_Identifiers(string gestor)
    {
        using var schema = Load(gestor);
        var identifiers = schema.RootElement
            .GetProperty("properties")
            .GetProperty("identificadores");

        var allowedTypes = identifiers.GetProperty("items")
            .GetProperty("properties")
            .GetProperty("tipo")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("CPF", allowedTypes);
        Assert.Contains("CNS", allowedTypes);
        Assert.Contains("RG", allowedTypes);
        Assert.Contains("CODIGO_BASE_ORIGEM", allowedTypes);
        Assert.Contains("UUID_JORNADA", allowedTypes);
        Assert.Contains("OUTRO", allowedTypes);
    }

    [Theory]
    [MemberData(nameof(Gestores))]
    public void V4_Keeps_Mother_Name_Optional(string gestor)
    {
        using var schema = Load(gestor);
        var required = schema.RootElement.GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();

        Assert.DoesNotContain("nomeMae", required);
    }

    private static JsonDocument Load(string gestor)
    {
        var root = FindSolutionRoot();
        var path = Path.Combine(root, "config", "contracts", "gestores", gestor, "pessoa", "v4", "pessoa.schema.json");
        Assert.True(File.Exists(path), $"Contrato Pessoa v4 ausente para {gestor}: {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "config", "contracts")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Solution root not found.");
    }
}
