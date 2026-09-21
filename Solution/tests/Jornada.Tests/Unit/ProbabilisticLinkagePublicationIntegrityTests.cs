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

    [Test]
    public void Progressive_publication_separates_raw_score_from_operational_decision()
    {
        var sql = ProbabilisticLinkageBatchRunner.ProgressivePublicationSql();

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("resultado_publicacao"));
            Assert.That(sql, Does.Contain("pessoa_uuid_publicado"));
            Assert.That(sql, Does.Contain("LINKAGE_PROGRESSIVE_PUBLICATION_V1"));
            Assert.That(sql, Does.Contain("NOVA_IDENTIDADE_APOS_BUSCA_COMPLETA"));
            Assert.That(sql, Does.Contain("SEM_ORIGEM_PERSISTENTE_PARA_NOVA_IDENTIDADE"));
            Assert.That(sql, Does.Contain("REFERENCIA_PROGRESSIVA_PRESERVADA"));
            Assert.That(sql, Does.Contain("DESTINO_LINKAGE_NAO_ESTABELECIDO"));
            Assert.That(sql, Does.Contain("sp_publicar_resolucao_progressiva_linkage"));
            Assert.That(sql, Does.Contain("CPF_DETERMINISTICO"));
            Assert.That(sql, Does.Contain("NIS_DETERMINISTICO"));
            Assert.That(sql, Does.Contain("UUID_JORNADA_RETROALIMENTACAO"));
            Assert.That(sql, Does.Not.Contain("SET status=N'RESOLVIDO'"));
            Assert.That(sql, Does.Not.Contain("SET pessoa_uuid_resolvido="));
        });
    }

    [Test]
    public void Progressive_publication_only_promotes_initial_uuid_after_explicit_no_candidate()
    {
        var sql = ProbabilisticLinkageBatchRunner.ProgressivePublicationSql();

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("c.raw_status=N'NAO_RESOLVIDO'"));
            Assert.That(sql, Does.Contain("c.raw_motivo LIKE N'SEM_CANDIDATO_%'"));
            Assert.That(sql, Does.Contain("c.initial_uuid"));
            Assert.That(sql, Does.Contain("c.ultimo_destino_externo_uuid IS NULL"));
            Assert.That(sql, Does.Contain("N'ASSOCIACAO_EXISTENTE'"));
            Assert.That(sql, Does.Contain("destino_estabelecido=1"));
            Assert.That(sql, Does.Contain("THROW 51821"));
        });
    }
}
