using System.Text.Json;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class TerritorialSemanticsDocumentationTests
{
    [Test]
    public void Residential_address_and_territorial_reference_keep_distinct_semantics()
    {
        var root = FindRepositoryRoot();
        var catalogPath = Path.Combine(root, "Solution", "config", "catalog", "atributos-transversais.json");
        var territorializationPath = Path.Combine(root, "Solution", "docs", "Territorializacao_Fase1.md");
        var docsIndexPath = Path.Combine(root, "Solution", "docs", "README.md");
        var requirementsPath = Path.Combine(root, "Documentos", "Requisitos", "01_Requisitos_de_Negocio_Jornada_v1.1.md");
        var candidateSpecificationPath = Path.Combine(root, "Documentos", "Especificacao_Tecnica_Jornada_Candidata.md");
        var ddlPath = Path.Combine(root, "Solution", "database", "Jornada_Fase1.sql");
        var seedPath = Path.Combine(root, "Solution", "database", "Jornada_Seed_Dev.sql");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(catalogPath), Is.True);
            Assert.That(File.Exists(territorializationPath), Is.True);
            Assert.That(File.Exists(docsIndexPath), Is.True);
            Assert.That(File.Exists(requirementsPath), Is.True);
            Assert.That(File.Exists(candidateSpecificationPath), Is.True);
            Assert.That(File.Exists(ddlPath), Is.True);
            Assert.That(File.Exists(seedPath), Is.True);
        });

        using var catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
        var attributes = catalog.RootElement.GetProperty("atributos").EnumerateArray().ToArray();
        var residentialAddress = attributes.Single(a => a.GetProperty("codigo").GetString() == "ENDERECO_RESIDENCIAL");
        var territorial = attributes.Single(a => a.GetProperty("codigo").GetString() == "REFERENCIA_TERRITORIAL");

        var residentialAddressDescription = residentialAddress.GetProperty("descricao").GetString();
        var territorialDescription = territorial.GetProperty("descricao").GetString();
        var territorialization = File.ReadAllText(territorializationPath);
        var docsIndex = File.ReadAllText(docsIndexPath);
        var requirements = File.ReadAllText(requirementsPath);
        var candidateSpecification = File.ReadAllText(candidateSpecificationPath);
        var ddl = File.ReadAllText(ddlPath);
        var seed = File.ReadAllText(seedPath);

        Assert.Multiple(() =>
        {
            Assert.That(residentialAddressDescription, Does.Contain("Atributo contratual de endereço residencial informado pela origem"));
            Assert.That(residentialAddressDescription, Does.Contain("não o redefine automaticamente como endereço de residência"));
            Assert.That(residentialAddressDescription, Does.Not.Contain("Endereço cadastral de residência"));

            Assert.That(territorialDescription, Does.Contain("superfície canônica da visualização territorial"));
            Assert.That(territorialDescription, Does.Contain("DOMICILIAR").And.Contain("ACOLHIMENTO_INSTITUCIONAL").And.Contain("REFERENCIA_TERRITORIAL_DECLARADA"));

            Assert.That(territorialization, Does.Contain("Endereço residencial e Referência Territorial são conceitos distintos"));
            Assert.That(territorialization, Does.Contain("não a redefine automaticamente como **endereço de residência**"));
            Assert.That(territorialization, Does.Contain("superfície canônica da visualização territorial"));
            Assert.That(territorialization, Does.Contain("não deve promover automaticamente qualquer endereço cadastral a território analítico"));

            Assert.That(docsIndex, Does.Contain("A Referência Territorial permanece a superfície territorial única da visualização"));
            Assert.That(docsIndex, Does.Contain("atributo contratual de endereço residencial informado pela origem"));
            Assert.That(docsIndex, Does.Contain("não é redefinido automaticamente como endereço de residência"));
            Assert.That(docsIndex, Does.Not.Contain("`ENDERECO_RESIDENCIAL` permanece cadastral"));

            Assert.That(requirements, Does.Contain("RN-015 - Manter Referência Territorial separada de endereço civil"));
            Assert.That(requirements, Does.Contain("não se confunde com ENDERECO_RESIDENCIAL nem com endereço de correspondência"));

            Assert.That(candidateSpecification, Does.Contain("a Jornada não o redefine automaticamente como “endereço de residência”"));
            Assert.That(candidateSpecification, Does.Contain("referência territorial é informação temporal própria selecionada para territorialização"));
            Assert.That(candidateSpecification, Does.Not.Contain("endereço residencial é atributo cadastral que descreve endereço de residência"));

            Assert.That(ddl, Does.Contain("A camada territorial usa exclusivamente o snapshot de REFERENCIA_TERRITORIAL selecionado"));

            Assert.That(seed, Does.Not.Contain("O snapshot da classificação fica associado à observação do ENDERECO_RESIDENCIAL"));
            Assert.That(seed, Does.Contain("O snapshot da classificação territorial fica associado à REFERENCIA_TERRITORIAL selecionada"));
            Assert.That(seed, Does.Contain("fonte_semantica=ENDERECO_RESIDENCIAL"));
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
