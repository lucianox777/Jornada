using System.Text.Json;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class RequirementsCurrentMapTests
{
    [Test]
    public void CurrentRequirementsMap_CoversConsolidatedBaselineAndHistoricalAdendumIsNotNormative()
    {
        var root = FindRepositoryRoot();
        var requirements = Path.Combine(root, "Documentos", "Requisitos");

        using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(requirements, "requirements-map.json")));
        using var current = JsonDocument.Parse(File.ReadAllText(Path.Combine(requirements, "requirements-map-v1.1.json")));

        Assert.Multiple(() =>
        {
            Assert.That(baseline.RootElement.GetProperty("counts").GetProperty("RF").GetInt32(), Is.EqualTo(50));
            Assert.That(baseline.RootElement.GetProperty("counts").GetProperty("RNF").GetInt32(), Is.EqualTo(33));
            Assert.That(current.RootElement.GetProperty("status").GetString(), Is.EqualTo("VIGENTE"));
            Assert.That(current.RootElement.GetProperty("baselineMap").GetString(), Is.EqualTo("requirements-map.json"));
            Assert.That(current.RootElement.GetProperty("counts").GetProperty("RN").GetInt32(), Is.EqualTo(36));
            Assert.That(current.RootElement.GetProperty("counts").GetProperty("RF").GetInt32(), Is.EqualTo(56));
            Assert.That(current.RootElement.GetProperty("counts").GetProperty("RNF").GetInt32(), Is.EqualTo(37));
            Assert.That(current.RootElement.GetProperty("counts").GetProperty("RT").GetInt32(), Is.EqualTo(65));
        });

        var additiveRf = current.RootElement.GetProperty("additiveRf");
        var expectedRf = Enumerable.Range(51, 6).Select(n => $"RF-{n:000}").ToArray();
        Assert.That(additiveRf.EnumerateObject().Select(p => p.Name).ToArray(), Is.EqualTo(expectedRf));

        var additiveRnf = current.RootElement.GetProperty("additiveRnf");
        var expectedRnf = new[] { "RNF34-A", "RNF34-B", "RNF34-C", "RNF34-D" };
        Assert.That(additiveRnf.EnumerateObject().Select(p => p.Name).ToArray(), Is.EqualTo(expectedRnf));

        var rfText = File.ReadAllText(Path.Combine(requirements, "02_Requisitos_Funcionais_Jornada_v1.1.md"));
        foreach (var id in Enumerable.Range(1, 56).Select(n => $"RF-{n:000}"))
            Assert.That(rfText, Does.Contain($"## {id} "), $"RF consolidado ausente: {id}");

        var rnfText = File.ReadAllText(Path.Combine(requirements, "03_Requisitos_Nao_Funcionais_Jornada_v1.1.md"));
        foreach (var id in Enumerable.Range(1, 33).Select(n => $"RNF-{n:000}"))
            Assert.That(rnfText, Does.Contain($"## {id} "), $"RNF histórico normalizado ausente: {id}");
        foreach (var id in expectedRnf)
            Assert.That(rnfText, Does.Contain($"## {id} "), $"RNF aditivo consolidado ausente: {id}");

        var indexText = File.ReadAllText(Path.Combine(requirements, "00_Indice_Mestre_Requisitos_Jornada_v1.1.md"));
        Assert.Multiple(() =>
        {
            Assert.That(indexText, Does.Contain("um único documento por número"));
            Assert.That(indexText, Does.Contain("não devem ser lidos cumulativamente"));
            Assert.That(indexText, Does.Contain("RNF-001` a `RNF-033"));
        });

        var superseded = current.RootElement.GetProperty("supersededDocuments")
            .EnumerateArray()
            .Select(x => x.GetString())
            .ToArray();
        Assert.That(superseded, Does.Contain("06_Adendo_RF_RNF_Linkage_Calibracao_Avaliacao_v1.0.md"));

        var adendum = File.ReadAllText(Path.Combine(requirements, "06_Adendo_RF_RNF_Linkage_Calibracao_Avaliacao_v1.0.md"));
        Assert.Multiple(() =>
        {
            Assert.That(adendum, Does.Contain("HISTÓRICO — SUPERADO PELA CONSOLIDAÇÃO RF/RNF v1.1"));
            Assert.That(adendum, Does.Contain("não é fonte normativa concorrente"));
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
