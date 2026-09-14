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
            new SplinkComparisonLevelEstimate("nome", "high", 0.08m, 0.04m)
        };

        var m = SplinkCalibrationExchange.ImportM("4.x-test", levels);
        var u = SplinkCalibrationExchange.ImportU("4.x-test", levels);

        Assert.Multiple(() =>
        {
            Assert.That(m.Estimator, Is.EqualTo(ParameterEstimatorKind.Splink));
            Assert.That(u.Estimator, Is.EqualTo(ParameterEstimatorKind.Splink));
            Assert.That(m.Values["M_NOME_EXACT"], Is.EqualTo(0.9m));
            Assert.That(u.Values["U_NOME_HIGH"], Is.EqualTo(0.04m));
        });
    }

    [Test]
    public void DeserializeRunnerResult_ValidatesSemanticContractAndImportsSameNameStates()
    {
        const string json = """
        {
          "schema_version": "JORNADA_SPLINK_ESTIMATES_V1",
          "source_schema_version": "JORNADA_SPLINK_EXCHANGE_V1",
          "splink_version": "4.0.17",
          "scope": "NOME",
          "nominal_semantics_version": "IDENTITY_NAME_STATES_V1",
          "seed": 42,
          "max_pairs": 10000000,
          "estimates": [
            {"feature":"NOME","level":"EXACT","m_probability":0.80,"u_probability":0.01},
            {"feature":"NOME","level":"HIGH","m_probability":0.10,"u_probability":0.02},
            {"feature":"NOME","level":"MEDIUM","m_probability":0.07,"u_probability":0.07},
            {"feature":"NOME","level":"LOW","m_probability":0.03,"u_probability":0.90}
          ]
        }
        """;

        var result = SplinkCalibrationExchange.DeserializeRunnerResult(json);
        var m = SplinkCalibrationExchange.ImportM(result);
        var u = SplinkCalibrationExchange.ImportU(result);

        Assert.Multiple(() =>
        {
            Assert.That(result.SplinkVersion, Is.EqualTo("4.0.17"));
            Assert.That(m.Values.Keys, Is.EquivalentTo(new[] { "M_NOME_EXACT", "M_NOME_HIGH", "M_NOME_MEDIUM", "M_NOME_LOW" }));
            Assert.That(u.Values.Keys, Is.EquivalentTo(new[] { "U_NOME_EXACT", "U_NOME_HIGH", "U_NOME_MEDIUM", "U_NOME_LOW" }));
        });
    }

    [Test]
    public void DeserializeRunnerResult_RejectsDifferentNominalSemantics()
    {
        const string json = """
        {
          "schema_version": "JORNADA_SPLINK_ESTIMATES_V1",
          "source_schema_version": "JORNADA_SPLINK_EXCHANGE_V1",
          "splink_version": "4.0.17",
          "scope": "NOME",
          "nominal_semantics_version": "OTHER",
          "seed": 42,
          "max_pairs": 1000,
          "estimates": [{"feature":"NOME","level":"EXACT","m_probability":0.8,"u_probability":0.1}]
        }
        """;

        Assert.That(
            () => SplinkCalibrationExchange.DeserializeRunnerResult(json),
            Throws.InvalidOperationException.With.Message.Contains("Semântica nominal incompatível"));
    }
}
