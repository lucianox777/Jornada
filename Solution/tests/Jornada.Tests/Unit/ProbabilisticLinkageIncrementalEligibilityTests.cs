using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class ProbabilisticLinkageIncrementalEligibilityTests
{
    [Test]
    public void Incremental_reconsiders_unresolved_and_conflict_when_candidate_side_changes()
    {
        var sql = ProbabilisticLinkageBatchRunner.EligibleFromWhereSql();

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("WHERE po.cpf IS NULL"));
            Assert.That(sql, Does.Contain("@mode='INCREMENTAL'"));
            Assert.That(sql, Does.Contain("vc.status IN('NAO_RESOLVIDO','CONFLITO')"));
            Assert.That(sql, Does.Contain("vc.metodo_resolucao='PENDENTE_PROBABILISTICO'"));
            Assert.That(sql, Does.Contain("vc.pessoa_observacao_id IS NULL"));
        });
    }

    [Test]
    public void Replay_remains_explicitly_separate_from_incremental()
    {
        var sql = ProbabilisticLinkageBatchRunner.EligibleFromWhereSql();

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("@mode='REPLAY'"));
            Assert.That(sql, Does.Contain("@mode='ON_DEMAND'"));
            Assert.That(sql, Does.Contain("@mode IN('FULL','MODEL_VALIDATION')"));
        });
    }
}
