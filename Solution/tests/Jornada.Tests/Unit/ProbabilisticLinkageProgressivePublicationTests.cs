using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class ProbabilisticLinkageProgressivePublicationTests
{
    [Test]
    public void Publication_policy_separates_raw_score_from_operational_resolution()
    {
        var sql = ProbabilisticLinkageBatchRunner.ProgressivePublicationSql();

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("resultado_publicacao"));
            Assert.That(sql, Does.Contain("pessoa_uuid_publicado"));
            Assert.That(sql, Does.Contain("status_publicacao"));
            Assert.That(sql, Does.Contain("NOVA_IDENTIDADE_APOS_BUSCA_COMPLETA"));
            Assert.That(sql, Does.Contain("DESTINO_LINKAGE_NAO_ESTABELECIDO"));
            Assert.That(sql, Does.Contain("Origem persistente sem initial_uuid"));
            Assert.That(sql, Does.Contain("sp_publicar_resolucao_progressiva_linkage"));
            Assert.That(sql, Does.Contain("origem_protegida=0"));
            Assert.That(sql, Does.Contain("progressiva_versao IS NULL"));
        });
    }

    [Test]
    public void Migration_requires_complete_run_and_never_uses_initial_uuid_as_similarity_evidence()
    {
        var migration = File.ReadAllText(Path.Combine(FindSolutionRoot(), "database", "migrations",
            "20260919_Linkage_Publicacao_Progressiva.sql"));

        Assert.Multiple(() =>
        {
            Assert.That(migration, Does.Contain("@run_avaliados<>@run_elegiveis"));
            Assert.That(migration, Does.Contain("@run_itens<>@run_elegiveis"));
            Assert.That(migration, Does.Contain("@raw_motivo NOT LIKE N'SEM_CANDIDATO_%'"));
            Assert.That(migration, Does.Contain("@canonical_uuid<>@initial"));
            Assert.That(migration, Does.Contain("Destino probabilístico não é referência canônica estabelecida"));
            Assert.That(migration, Does.Contain("Vínculo determinístico/governado tem precedência"));
            Assert.That(migration, Does.Contain("resultado_publicacao IN(N'ASSOCIACAO_EXISTENTE',N'NOVA_IDENTIDADE',N'INDEFINIDA')"));
            Assert.That(migration, Does.Contain("r.pessoa_uuid_publicado AS pessoa_uuid_resolvido"));
            Assert.That(migration, Does.Contain("r.pessoa_uuid_resolvido=r.pessoa_uuid_publicado"));
            Assert.That(migration, Does.Contain("END AS score_publicacao"));
            Assert.That(migration, Does.Contain("p.score_publicacao,p.status"));
            Assert.That(migration, Does.Not.Contain("score_melhor=initial_uuid").IgnoreCase);
            Assert.That(migration, Does.Not.Contain("score_segundo=initial_uuid").IgnoreCase);
        });
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Jornada.sln"))
                && Directory.Exists(Path.Combine(directory.FullName, "database", "migrations")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Solution root not found.");
    }
}
