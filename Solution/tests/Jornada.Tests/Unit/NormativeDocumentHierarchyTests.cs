using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class NormativeDocumentHierarchyTests
{
    [Test]
    public void PublishedSpecificationHierarchy_IsExplicitAndDoesNotInventV364()
    {
        var root = FindRepositoryRoot();
        var documentos = Path.Combine(root, "Documentos");
        var docsReadmePath = Path.Combine(documentos, "README.md");
        var solutionDocsReadmePath = Path.Combine(root, "Solution", "docs", "README.md");
        var releaseInfoPath = Path.Combine(root, "RELEASE_INFO.txt");
        var specV362Docx = Path.Combine(documentos, "Especificacao_Tecnica_Jornada_v3.62.docx");
        var specV362Pdf = Path.Combine(documentos, "Especificacao_Tecnica_Jornada_v3.62.pdf");
        var specV364Docx = Path.Combine(documentos, "Especificacao_Tecnica_Jornada_v3.64.docx");
        var specV364Pdf = Path.Combine(documentos, "Especificacao_Tecnica_Jornada_v3.64.pdf");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(docsReadmePath), Is.True);
            Assert.That(File.Exists(solutionDocsReadmePath), Is.True);
            Assert.That(File.Exists(releaseInfoPath), Is.True);
            Assert.That(File.Exists(specV362Docx), Is.True, "Especificação Técnica v3.62 DOCX publicada ausente.");
            Assert.That(File.Exists(specV362Pdf), Is.True, "Especificação Técnica v3.62 PDF publicada ausente.");
            Assert.That(File.Exists(specV364Docx), Is.False, "Não criar Especificação Técnica v3.64 por inferência.");
            Assert.That(File.Exists(specV364Pdf), Is.False, "Não criar Especificação Técnica v3.64 por inferência.");
        });

        var docsReadme = File.ReadAllText(docsReadmePath);
        var solutionDocsReadme = File.ReadAllText(solutionDocsReadmePath);
        var releaseInfo = File.ReadAllText(releaseInfoPath);

        Assert.Multiple(() =>
        {
            Assert.That(releaseInfo, Does.Contain("base_normativa=v3.64"));
            Assert.That(releaseInfo, Does.Contain("schema_base_normativa=v3.62"));
            Assert.That(docsReadme, Does.Contain("Especificacao_Tecnica_Jornada_v3.62.docx"));
            Assert.That(docsReadme, Does.Contain("Não existe `Especificacao_Tecnica_Jornada_v3.64.*`"));
            Assert.That(solutionDocsReadme, Does.Contain("Especificacao_Tecnica_Jornada_v3.62.docx"));
            Assert.That(solutionDocsReadme, Does.Not.Contain("A fonte normativa é `../../Documentos/Especificacao_Tecnica_Jornada_v3.64.docx`"));
            Assert.That(solutionDocsReadme, Does.Contain("não implica que exista um arquivo `Especificacao_Tecnica_Jornada_v3.64.*`"));
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
