using System.Text.Json;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class TerritorialSemanticsDocumentationTests
{
    [Test]
    public void Residence_and_territorial_reference_remain_distinct_concepts()
    {
        var root = FindRepositoryRoot();
        var catalogPath = Path.Combine(root, "Solution", "config", "catalog", "atributos-transversais.json");
        var territorializationPath = Path.Combine(root, "Solution", "docs", "Territorializacao_Fase1.md");
        var requirementsPath = Path.Combine(root, "Documentos", "Requisitos", "01_Requisitos_de_Negocio_Jornada_v1.1.md");
        var ddlPath = Path.Combine(root, "Solution", "database", "Jornada_Fase1.sql");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(catalogPath), Is.True);
            Assert.That(File.Exists(territorializationPath), Is.True);
            Assert.That(File.Exists(requirementsPath), Is.True);
            Assert.That(File.Exists(ddlPath), Is.True);
        });

        using var catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
        var attributes = catalog.RootElement.GetProperty("atributos").EnumerateArray().ToArray();
        var residence = attributes.Single(a => a.GetProperty("codigo").GetString() == "ENDERECO_RESIDENCIAL");
        var territorial = attributes.Single(a => a.GetProperty("codigo").GetString() == "REFERENCIA_TERRITORIAL");

        var residenceDescription = residence.GetProperty("descricao").GetString();
        var territorialDescription = territorial.GetProperty("descricao").GetString();
        var territorialization = File.ReadAllText(territorializationPath);
        var requirements = File.ReadAllText(requirementsPath);
        var ddl = File.ReadAllText(ddlPath);

        Assert.Multiple(() =>
        {
            Assert.That(residenceDescription, Does.Contain("Endereço cadastral de residência"));
            Assert.That(territorialDescription, Does.Contain("não é sinônimo de domicílio civil"));
            Assert.That(territorialDescription, Does.Contain("DOMICILIAR").And.Contain("ACOLHIMENTO_INSTITUCIONAL").And.Contain("REFERENCIA_TERRITORIAL_DECLARADA"));

            Assert.That(territorialization, Does.Contain("Residência não é Referência Territorial"));
            Assert.That(territorialization, Does.Contain("não deve ser tratado como sinônimo de `REFERENCIA_TERRITORIAL`"));
            Assert.That(territorialization, Does.Contain("a Jornada não deve promover automaticamente qualquer endereço cadastral a território analítico"));

            Assert.That(requirements, Does.Contain("RN-015 - Manter Referência Territorial separada de endereço civil"));
            Assert.That(requirements, Does.Contain("não se confunde com ENDERECO_RESIDENCIAL nem com endereço de correspondência"));

            Assert.That(ddl, Does.Contain("ENDERECO_RESIDENCIAL é atributo cadastral de endereço de residência"));
            Assert.That(ddl, Does.Contain("A camada territorial usa exclusivamente o snapshot de REFERENCIA_TERRITORIAL selecionado"));
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
