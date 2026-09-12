using System.IO.Compression;
using System.Xml.Linq;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class InstitutionalAcronymRegressionTests
{
    private const string LegacyAcronym = "SGM/SPE";
    private const string CanonicalAcronym = "SGM/SEPE";

    [Test]
    public void CurrentInstitutionalArtifacts_UseCanonicalAcronymAndRejectLegacyForm()
    {
        var root = FindRepositoryRoot();
        var requirements = Path.Combine(root, "Documentos", "Requisitos");

        var textArtifacts = Directory.GetFiles(requirements, "*_v1.1.md", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        textArtifacts.Add(Path.Combine(root, "Documentos", "Anexo_Modelo_Fisico_Jornada_v1.40.md"));

        Assert.That(textArtifacts, Is.Not.Empty);
        foreach (var path in textArtifacts)
        {
            Assert.That(File.Exists(path), Is.True, $"Artefato vigente ausente: {path}");
            Assert.That(
                File.ReadAllText(path),
                Does.Not.Contain(LegacyAcronym).IgnoreCase,
                $"Sigla institucional legada encontrada em {path}");
        }

        var businessRequirements = File.ReadAllText(
            Path.Combine(requirements, "01_Requisitos_de_Negocio_Jornada_v1.1.md"));
        Assert.That(
            businessRequirements,
            Does.Contain(CanonicalAcronym),
            "O requisito de negócio vigente deve identificar a área requisitante pela sigla canônica SGM/SEPE.");

        var docxArtifacts = Directory.GetFiles(requirements, "*_v1.1.docx", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        docxArtifacts.Add(Path.Combine(root, "Documentos", "Anexo_Modelo_Fisico_Jornada_v1.40.docx"));

        Assert.That(docxArtifacts, Is.Not.Empty);
        foreach (var path in docxArtifacts)
        {
            Assert.That(File.Exists(path), Is.True, $"Artefato DOCX vigente ausente: {path}");
            Assert.That(
                ReadDocxText(path),
                Does.Not.Contain(LegacyAcronym).IgnoreCase,
                $"Sigla institucional legada encontrada em {path}");
        }
    }

    private static string ReadDocxText(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var text = new System.Text.StringBuilder();

        foreach (var entry in archive.Entries
                     .Where(entry => entry.FullName.StartsWith("word/", StringComparison.Ordinal)
                                     && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(entry => entry.FullName, StringComparer.Ordinal))
        {
            using var stream = entry.Open();
            var document = XDocument.Load(stream);
            foreach (var node in document.DescendantNodes().OfType<XText>())
                text.Append(node.Value);
            text.AppendLine();
        }

        return text.ToString();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Documentos"))
                && Directory.Exists(Path.Combine(directory.FullName, "Solution")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Raiz do repositório Jornada não encontrada.");
    }
}
