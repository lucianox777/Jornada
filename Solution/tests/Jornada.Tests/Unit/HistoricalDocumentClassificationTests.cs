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

    [Test]
    public void RequirementsV11_IsTheSingleCurrentBaselineAndLegacyArtifactsAreHistorical()
    {
        var root = FindRepositoryRoot();
        var requisitos = Path.Combine(root, "Documentos", "Requisitos");
        var readmePath = Path.Combine(requisitos, "README.md");
        var currentIndex = Path.Combine(requisitos, "00_Indice_Mestre_Requisitos_Jornada_v1.1.md");
        var legacyIndex = Path.Combine(requisitos, "00_Indice_Mestre_Requisitos_Jornada_v1.0.md");
        var currentMap = Path.Combine(requisitos, "requirements-map-v1.1.json");
        var legacyMap = Path.Combine(requisitos, "requirements-map.json");
        var supersededAddendum = Path.Combine(requisitos, "06_Adendo_RF_RNF_Linkage_Calibracao_Avaliacao_v1.0.md");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(readmePath), Is.True, "README da família de requisitos ausente.");
            Assert.That(File.Exists(currentIndex), Is.True, "Índice mestre v1.1 corrente ausente.");
            Assert.That(File.Exists(legacyIndex), Is.True, "Baseline histórico v1.0 ausente.");
            Assert.That(File.Exists(currentMap), Is.True, "Mapa corrente v1.1 ausente.");
            Assert.That(File.Exists(legacyMap), Is.True, "Mapa baseline histórico ausente.");
            Assert.That(File.Exists(supersededAddendum), Is.True, "Adendo histórico/superseded ausente.");
        });

        var readme = File.ReadAllText(readmePath);
        var index = File.ReadAllText(currentIndex);
        var map = File.ReadAllText(currentMap);

        Assert.Multiple(() =>
        {
            Assert.That(readme, Does.Contain("00_Indice_Mestre_Requisitos_Jornada_v1.1"));
            Assert.That(readme, Does.Contain("baselines históricos").IgnoreCase);
            Assert.That(readme, Does.Contain("não devem ser lidos cumulativamente").IgnoreCase);
            Assert.That(readme, Does.Contain("requirements-map-v1.1.json"));
            Assert.That(readme, Does.Contain("mapa baseline histórico").IgnoreCase);
            Assert.That(readme, Does.Contain("06_Adendo_RF_RNF_Linkage_Calibracao_Avaliacao_v1.0.md"));
            Assert.That(readme, Does.Contain("superseded").IgnoreCase);
            Assert.That(readme, Does.Not.Contain("Use `00_Indice_Mestre_Requisitos_Jornada_v1.0` como porta de entrada"));
            Assert.That(readme, Does.Not.Contain("Especificação Técnica Jornada v3.63"));

            Assert.That(index, Does.Contain("PORTA DE ENTRADA DO BASELINE INSTITUCIONAL CONSOLIDADO"));
            Assert.That(index, Does.Contain("Os arquivos v1.0 permanecem no repositório exclusivamente como baselines históricos"));

            Assert.That(map, Does.Contain("\"status\": \"VIGENTE\""));
            Assert.That(map, Does.Contain("\"baselineMap\": \"requirements-map.json\""));
            Assert.That(map, Does.Contain("06_Adendo_RF_RNF_Linkage_Calibracao_Avaliacao_v1.0.md"));
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
