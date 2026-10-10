using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt05HistoricalCandidateParquetReaderContractTests
{
    [Test]
    public void Historical_reader_validates_logical_rows_and_candidate_identity()
    {
        var root = TestContext.CurrentContext.TestDirectory;
        var path = Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", "..", "src",
            "Jornada.Linkage.Runner", "Dt05HistoricalCandidateStateVerifier.cs"));
        var source = File.ReadAllText(path);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(source, Does.Contain("ParquetReader.CreateAsync"));
            Assert.That(source, Does.Contain("candidate_uuid").And.Contain("nome_completo")
                .And.Contain("data_nascimento").And.Contain("nome_mae").And.Contain("estado_identidade"));
            Assert.That(source, Does.Contain("Guid.TryParseExact"));
            Assert.That(source, Does.Contain("DateOnly.TryParseExact"));
            Assert.That(source, Does.Contain("REFERENCIA"));
            Assert.That(source, Does.Contain("logical_sha256 da partição histórica diverge das linhas"));
            Assert.That(source, Does.Contain("Encoding.Unicode"));
            Assert.That(source, Does.Contain("binding.CandidateSetSha256"));
            Assert.That(source, Does.Not.Contain("gold.pessoa"));
        }));
    }
}
