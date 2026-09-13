using System.Text.Json;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class TerritorialSemanticsDocumentationTests
{
    [Test]
    public void Residential_address_and_territorial_reference_keep_explicit_semantics()
    {
        var root = FindRepositoryRoot();
        var catalogPath = Path.Combine(root, "Solution", "config", "catalog", "atributos-transversais.json");
        var territorializationPath = Path.Combine(root, "Solution", "docs", "Territorializacao_Fase1.md");
        var requirementsPath = Path.Combine(root, "Documentos", "Requisitos", "01_Requisitos_de_Negocio_Jornada_v1.1.md");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(catalogPath), Is.True);
            Assert.That(File.Exists(territorializationPath), Is.True);
            Assert.That(File.Exists(requirementsPath), Is.True);
        });

        using var catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
        var attributes = catalog.RootElement.GetProperty("atributos").EnumerateArray().ToArray();
        var residential = attributes.Single(a => a.GetProperty("codigo").GetString() == "ENDERECO_RESIDENCIAL");
        var territorial = attributes.Single(a => a.GetProperty("codigo").GetString() == "REFERENCIA_TERRITORIAL");

        var residentialDescription = residential.GetProperty("descricao").GetString();
        var territorialDescription = territorial.GetProperty("descricao").GetString();
        var territorialization = File.ReadAllText(territorializationPath);
        var requirements = File.ReadAllText(requirementsPath);

        Assert.Multiple(() =>
        {
            Assert.That(residentialDescription, Does.Contain("semântica declarada pela origem"));
            Assert.That(residentialDescription, Does.Contain("não o redefine automaticamente como 'endereço de residência'"));
            Assert.That(territorialDescription, Does.Contain("superfície canônica de visualização territorial"));
            Assert.That(territorialDescription, Does.Contain("DOMICILIAR").And.Contain("ACOLHIMENTO_INSTITUCIONAL").And.Contain("REFERENCIA_TERRITORIAL_DECLARADA"));

            Assert.That(territorialization, Does.Contain("Endereço residencial, endereço de residência e Referência Territorial"));
            Assert.That(territorialization, Does.Contain("não deve defini-lo por equivalência automática como “endereço de residência”"));
            Assert.That(territorialization, Does.Contain("a superfície canônica é a **Referência Territorial selecionada**"));
            Assert.That(territorialization, Does.Contain("não infere que `ENDERECO_RESIDENCIAL` e “endereço de residência” tenham o mesmo significado"));

            Assert.That(requirements, Does.Contain("RN-015 - Manter Referência Territorial separada de endereço civil"));
            Assert.That(requirements, Does.Contain("não se confunde com ENDERECO_RESIDENCIAL nem com endereço de correspondência"));
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt")) &&
                Directory.Exists(Path.Combine(current.FullName, "Solution")) &&
                Directory.Exists(Path.Combine(current.FullName, "Documentos")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        Assert.Fail("Raiz do repositório Jornada não encontrada a partir do diretório de testes.");
        return string.Empty;
    }
}
