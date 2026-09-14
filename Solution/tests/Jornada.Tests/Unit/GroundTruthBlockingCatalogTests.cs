using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class GroundTruthBlockingCatalogTests
{
    [Test]
    public void CurrentBlockingCatalogDoesNotLeakCnsWhenCnsLabelsEvaluation()
    {
        Assert.DoesNotThrow(() => GroundTruthIsolationPolicy.EnsureNoLabelLeakage(
            GroundTruthSource.Cns,
            BlockingCandidateFeatureCatalog.CalibratorCandidates,
            new[] { "NOME", "NOME_MAE", "DATA_NASCIMENTO" }));
    }

    [Test]
    public void CurrentBlockingCatalogDoesNotUseCpfAsCandidateFeatureEither()
    {
        Assert.DoesNotThrow(() => GroundTruthIsolationPolicy.EnsureNoLabelLeakage(
            GroundTruthSource.Cpf,
            BlockingCandidateFeatureCatalog.CalibratorCandidates,
            new[] { "NOME", "NOME_MAE", "DATA_NASCIMENTO" }));
    }
}
