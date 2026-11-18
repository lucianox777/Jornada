namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9Dt10BatchPublicationGuardsTests
{
    private static string Migration()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Solution")))
            dir = dir.Parent;
        if (dir is null) throw new DirectoryNotFoundException("Solution");
        var path = Path.Combine(dir.FullName, "Solution", "database", "migrations",
            "20261029_Linkage_NoCandidate_Literal_Guards.sql");
        var sql = File.ReadAllText(path);
        var batchStart = sql.IndexOf(
            "CREATE OR ALTER PROCEDURE identidade.sp_publicar_resolucao_progressiva_linkage_lote",
            StringComparison.Ordinal);
        Assert.That(batchStart, Is.GreaterThanOrEqualTo(0));
        return sql[batchStart..];
    }

    [Test]
    public void Batch_requires_serializable_transaction_and_complete_materialized_run()
    {
        var sql = Migration();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(sql, Does.Contain("IF @@TRANCOUNT = 0"));
            Assert.That(sql, Does.Contain("IF @isolation <> 4"));
            Assert.That(sql, Does.Contain("Jornada.Linkage.Runner.Publish"));
            Assert.That(sql, Does.Contain("@avaliados<>@elegiveis"));
            Assert.That(sql, Does.Contain("@itens<>@elegiveis"));
            Assert.That(sql, Does.Contain("@resultados<>@elegiveis"));
            Assert.That(sql, Does.Contain("THROW 51818"));
            Assert.That(sql, Does.Contain("THROW 51820"));
        }));
    }

    [Test]
    public void Batch_rejects_false_no_candidate_and_uncertain_promotion_before_events()
    {
        var sql = Migration();
        var contradiction = sql.IndexOf("THROW 51835", StringComparison.Ordinal);
        var eventWrite = sql.IndexOf("INSERT identidade.pessoa_origem_progressiva_evento",
            StringComparison.Ordinal);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(sql, Does.Contain("l.raw_motivo NOT LIKE N'SEM[_]CANDIDATO[_]%'"));
            Assert.That(sql, Does.Contain("r.melhor_candidato_uuid IS NOT NULL"));
            Assert.That(sql, Does.Contain("r.segundo_candidato_uuid IS NOT NULL"));
            Assert.That(sql, Does.Contain("r.pessoa_uuid_resolvido IS NOT NULL"));
            Assert.That(sql, Does.Contain("THROW 51810"));
            Assert.That(contradiction, Is.GreaterThanOrEqualTo(0));
            Assert.That(eventWrite, Is.GreaterThan(contradiction));
            Assert.That(sql, Does.Contain("THROW 51814"));
            Assert.That(sql, Does.Contain("THROW 51815"));
        }));
    }

    [Test]
    public void Batch_preserves_deterministic_precedence_and_origin_versioning()
    {
        var sql = Migration();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(sql, Does.Contain("MAX(x.protegido) OVER("));
            Assert.That(sql, Does.Contain("PARTITION BY x.pessoa_origem_id"));
            Assert.That(sql, Does.Contain("WHERE x.rn=1 AND x.origem_protegida=0"));
            Assert.That(sql, Does.Contain("THROW 51819"));
            Assert.That(sql, Does.Contain("THROW 51808"));
            Assert.That(sql, Does.Contain("THROW 51809"));
            Assert.That(sql, Does.Contain("THROW 51812"));
        }));
    }
}
