namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class HistoricalDocumentClassificationTests
{
    [Test]
    public void PendenciasV147_IsExplicitlyClassifiedAsHistorical()
    {
        var root = FindRepositoryRoot();
        var documentos = Path.Combine(root, "Documentos");
        var readmePath = Path.Combine(documentos, "README.md");
        var docxPath = Path.Combine(documentos, "Anexo_Pendencias_Desenvolvimento_Jornada_v1.47.docx");
        var pdfPath = Path.Combine(documentos, "Anexo_Pendencias_Desenvolvimento_Jornada_v1.47.pdf");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(docxPath), Is.True, "Snapshot histórico DOCX ausente.");
            Assert.That(File.Exists(pdfPath), Is.True, "Snapshot histórico PDF ausente.");
            Assert.That(File.Exists(readmePath), Is.True, "Índice documental ausente.");
        });

        var index = File.ReadAllText(readmePath);
        Assert.Multiple(() =>
        {
            Assert.That(index, Does.Contain("Documentos/Anexo_Pendencias_Desenvolvimento_Jornada_v1.47.docx"));
            Assert.That(index, Does.Contain("Documentos/Anexo_Pendencias_Desenvolvimento_Jornada_v1.47.pdf"));
            Assert.That(index, Does.Contain("snapshot histórico de pendências").IgnoreCase);
            Assert.That(index, Does.Contain("não constituem o backlog corrente").IgnoreCase);
        });
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
