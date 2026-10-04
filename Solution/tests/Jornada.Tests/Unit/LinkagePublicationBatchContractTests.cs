namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class LinkagePublicationBatchContractTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "Solution")))
            d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException();
    }

    [Test]
    public void Linkage_publication_is_batched_and_emits_phase_progress()
    {
        var root = Root();
        var runner = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Linkage.Runner", "ProbabilisticLinkageBatchRunner.cs"));
        var batchSql = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Linkage.Runner", "LinkagePublicationBatchSql.cs"));
        var blocking = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Operational.Sql", "BlockingProjectionPersistence.cs"));
        var localCluster = File.ReadAllText(Path.Combine(
            root, "Solution", "scripts", "local-cluster.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(runner, Does.Not.Contain("DECLARE gold_progressiva CURSOR"));
            Assert.That(runner, Does.Not.Contain("foreach (var uuid in newReferences)"));
            Assert.That(runner, Does.Contain("LinkagePublicationBatchSql.RecomposeAffectedGoldSql"));
            Assert.That(runner, Does.Contain("LinkagePublicationBatchSql.RefreshServingAssignmentsSql"));
            Assert.That(runner, Does.Contain("RefreshSqlServerBatchAsync"));
            Assert.That(runner, Does.Contain("fase 1/4 - aplicando decisão progressiva"));
            Assert.That(runner, Does.Contain("fase 2/4 - recompondo Gold em lote"));
            Assert.That(runner, Does.Contain("fase 3/4 - sincronizando fatos/Serving em lote"));
            Assert.That(runner, Does.Contain("fase 4/4 - atualizando blocking em lote"));
            Assert.That(runner, Does.Contain("commit concluído"));

            Assert.That(batchSql, Does.Contain("#gold_obs_ids"));
            Assert.That(batchSql, Does.Contain("MERGE gold.pessoa WITH (HOLDLOCK)"));
            Assert.That(batchSql, Does.Contain("CREATE UNIQUE CLUSTERED INDEX IX_gold_obs_ids_uuid_obs"));
            Assert.That(batchSql, Does.Not.Contain("CURSOR"));

            Assert.That(blocking, Does.Contain("public static async Task RefreshSqlServerBatchAsync"));
            Assert.That(blocking, Does.Contain("#jornada_blocking_refresh_uuid"));
            Assert.That(blocking, Does.Contain("new SqlBulkCopy"));
            Assert.That(blocking, Does.Contain("DELETE bc"));
            Assert.That(blocking, Does.Contain("PARTITION BY vc.pessoa_uuid"));

            Assert.That(localCluster, Does.Contain("execução/publicação do linkage"));
            Assert.That(localCluster, Does.Contain("Logs do jornada-node2"));
        });
    }
}
