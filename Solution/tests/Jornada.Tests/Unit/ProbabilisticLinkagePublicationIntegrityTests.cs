using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class ProbabilisticLinkagePublicationIntegrityTests
{
    [Test]
    public void Publication_guard_requires_materialized_universe_model_identity_and_membership()
    {
        var sql = ProbabilisticLinkageBatchRunner.PublicationIntegrityGuardSql();

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("@run_elegiveis<>@elegiveis"));
            Assert.That(sql, Does.Contain("@itens<>@elegiveis"));
            Assert.That(sql, Does.Contain("r.modelo_id<>@run_modelo_id"));
            Assert.That(sql, Does.Contain("r.modelo_versao<>@run_modelo_versao"));
            Assert.That(sql, Does.Contain("i.pessoa_observacao_id=r.pessoa_observacao_id"));
            Assert.That(sql, Does.Contain("THROW 51108"));
            Assert.That(sql, Does.Contain("THROW 51109"));
            Assert.That(sql, Does.Contain("THROW 51110"));
        });
    }
}
