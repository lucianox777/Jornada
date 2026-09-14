using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class SplinkCalibrationExchangeTests
{
    [Test]
    public void Export_UsesSplinkLabelShapeAndKeepsPartitionIsolated()
    {
        var snapshot = IbgeTypedNameFrequencyCatalog.Create(
            "IBGE-TEST",
            IbgeGeographicScope.Brazil,
            null,
            new[]
            {
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "MARIA", 100),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "MARINA", 90),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "SILVA", 100),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "SOUZA", 90)
            });
        var pair = new IbgeNominalBenchmarkPair(
            "PAIR-1",
            BenchmarkPartition.Train,
            true,
            new IbgeBenchmarkPerson("P1", "MARIA", "SILVA"),
            new IbgeBenchmarkPerson("P1", "MARINA", "SILVA"),
            Array.Empty<NominalSubstitution>());

        var package = SplinkCalibrationExchange.Export(snapshot, new[] { pair }, BenchmarkPartition.Train, 42);
        var json = SplinkCalibrationExchange.Serialize(package);

        Assert.Multiple(() =>
        {
            Assert.That(package.Records, Has.Count.EqualTo(2));
            Assert.That(package.Labels, Has.Count.EqualTo(1));
            Assert.That(package.Labels[0].ClericalMatchScore, Is.EqualTo(1m));
            Assert.That(package.Records.All(x => x.SourceDataset == SplinkCalibrationExchange.SourceDataset), Is.True);
            Assert.That(json, Does.Contain("\"unique_id_l\""));
            Assert.That(json, Does.Contain("\"clerical_match_score\""));
        });
    }

    [Test]
    public void Import_CreatesVersionedSplinkMAndUEstimatesForFactorialFactory()
    {
        var levels = new[]
        {
            new SplinkComparisonLevelEstimate("nome", "exact", 0.9m, 0.01m),
            new SplinkComparisonLevelEstimate("nome", "fuzzy high", 0.08m, 0.04m)
        };

        var m = SplinkCalibrationExchange.ImportM("4.x-test", levels);
        var u = SplinkCalibrationExchange.ImportU("4.x-test", levels);

        Assert.Multiple(() =>
        {
            Assert.That(m.Estimator, Is.EqualTo(ParameterEstimatorKind.Splink));
            Assert.That(u.Estimator, Is.EqualTo(ParameterEstimatorKind.Splink));
            Assert.That(m.Values["M_NOME_EXACT"], Is.EqualTo(0.9m));
            Assert.That(u.Values["U_NOME_FUZZY_HIGH"], Is.EqualTo(0.04m));
        });
    }
}
