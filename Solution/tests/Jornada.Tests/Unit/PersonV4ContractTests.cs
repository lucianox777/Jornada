using System.Text.Json;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class PersonV4ContractTests
{
    private static readonly string[] Gestores = ["SEHAB", "SMADS", "SMDET", "SMS"];

    [TestCaseSource(nameof(Gestores))]
    public void V4_Requires_Demographic_Core_But_Not_Identifier(string gestor)
    {
        using var schema = Load(gestor);
        var required = schema.RootElement.GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(required, Does.Contain("nomeCompleto"));
            Assert.That(required, Does.Contain("dataNascimento"));
            Assert.That(required, Does.Not.Contain("cpf"));
            Assert.That(required, Does.Not.Contain("codigoPessoaOrigem"));
            Assert.That(required, Does.Not.Contain("identificadores"));
        });
    }

    [TestCaseSource(nameof(Gestores))]
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

        Assert.Multiple(() =>
        {
            Assert.That(allowedTypes, Does.Contain("CPF"));
            Assert.That(allowedTypes, Does.Contain("CNS"));
            Assert.That(allowedTypes, Does.Contain("RG"));
            Assert.That(allowedTypes, Does.Contain("CODIGO_BASE_ORIGEM"));
            Assert.That(allowedTypes, Does.Contain("UUID_JORNADA"));
            Assert.That(allowedTypes, Does.Contain("OUTRO"));
        });
    }

    [TestCaseSource(nameof(Gestores))]
    public void V4_Keeps_Mother_Name_Optional(string gestor)
    {
        using var schema = Load(gestor);
        var required = schema.RootElement.GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();

        Assert.That(required, Does.Not.Contain("nomeMae"));
    }

    private static JsonDocument Load(string gestor)
    {
        var root = FindSolutionRoot();
        var path = Path.Combine(root, "config", "contracts", "gestores", gestor, "pessoa", "v4", "pessoa.schema.json");
        Assert.That(File.Exists(path), Is.True, $"Contrato Pessoa v4 ausente para {gestor}: {path}");
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
