using System.Text.Json;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class RequirementsCurrentMapTests
{
    [Test]
    public void CurrentRequirementsMap_CoversCanonicalAdditionsAndHistoricalAdendumIsNotNormative()
    {
        var root = FindRepositoryRoot();
        var requirements = Path.Combine(root, "Documentos", "Requisitos");

        using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(requirements, "requirements-map.json")));
        using var current = JsonDocument.Parse(File.ReadAllText(Path.Combine(requirements, "requirements-map-v1.1.json")));

        Assert.Multiple(() =>
        {
            Assert.That(baseline.RootElement.GetProperty("counts").GetProperty("RF").GetInt32(), Is.EqualTo(50));
            Assert.That(baseline.RootElement.GetProperty("counts").GetProperty("RNF").GetInt32(), Is.EqualTo(33));
            Assert.That(current.RootElement.GetProperty("effective_counts").GetProperty("RF").GetInt32(), Is.EqualTo(56));
            Assert.That(current.RootElement.GetProperty("effective_counts").GetProperty("RNF_identifiers").GetInt32(), Is.EqualTo(37));
        });

        var additions = current.RootElement.GetProperty("current_additions");
        var rf = additions.GetProperty("RF").EnumerateArray().Select(x => x.GetString()).ToArray();
        var rnf = additions.GetProperty("RNF").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.That(rf, Is.EqualTo(new[] { "RF-051", "RF-052", "RF-053", "RF-054", "RF-055", "RF-056" }));
        Assert.That(rnf, Is.EqualTo(new[] { "RNF34-A", "RNF34-B", "RNF34-C", "RNF34-D" }));

        var rfText = File.ReadAllText(Path.Combine(requirements, "02_Requisitos_Funcionais_Jornada_v1.1.md"));
        foreach (var id in rf!) Assert.That(rfText, Does.Contain($"## {id} "));

        var rnfText = File.ReadAllText(Path.Combine(requirements, "03_Requisitos_Nao_Funcionais_Jornada_v1.1.md"));
        foreach (var id in rnf!) Assert.That(rnfText, Does.Contain($"## {id} "));

        var adendum = File.ReadAllText(Path.Combine(requirements, "06_Adendo_RF_RNF_Linkage_Calibracao_Avaliacao_v1.0.md"));
        Assert.Multiple(() =>
        {
            Assert.That(adendum, Does.Contain("SUPERADO / HISTÓRICO — NÃO NORMATIVO"));
            Assert.That(adendum, Does.Contain("Em caso de divergência, prevalece a numeração dos documentos vigentes"));
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
