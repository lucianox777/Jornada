namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9ExistingIdentityPublicationBoundaryTests
{
    private static string ReadRepositoryFile(params string[] parts)
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Solution")))
            dir = dir.Parent;
        if (dir is null) throw new DirectoryNotFoundException("Solution");
        return File.ReadAllText(Path.Combine(new[] { dir.FullName, "Solution" }.Concat(parts).ToArray()));
    }

    [Test]
    public void V9_productive_entrypoint_remains_closed_and_does_not_use_legacy_publisher()
    {
        var runner = ReadRepositoryFile("src", "Jornada.Linkage.Runner", "ProbabilisticLinkageBatchRunner.cs");
        var start = runner.IndexOf("public Task<ProbabilisticLinkageRunSummary> RunV9Async(", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        // The explicitly controlled DEV entry now follows the productive one.
        // Do not include its authorized publication path in the productive guard.
        var end = runner.IndexOf("public async Task<ProbabilisticLinkageRunSummary> RunV9ControlledDevAsync(", start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start));
        var body = runner[start..end];
        Assert.Multiple((Action)(() =>
        {
            Assert.That(body, Does.Contain("boundary.EnsureProductiveCertified()"));
            Assert.That(body, Does.Contain("RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED"));
            Assert.That(body, Does.Not.Contain("RunCoreAsync("));
            Assert.That(body, Does.Not.Contain("RunV9ControlledDevAsync("));
            Assert.That(body, Does.Not.Contain("LinkagePublicationBatchSql"));
            Assert.That(body, Does.Not.Contain("sp_publicar_resolucao_progressiva_linkage_lote"));
        }));
    }

    [Test]
    public void Existing_publisher_distinguishes_empty_candidates_from_uncertain_candidates()
    {
        var runner = ReadRepositoryFile("src", "Jornada.Linkage.Runner", "ProbabilisticLinkageBatchRunner.cs");
        var sql = ReadRepositoryFile("database", "Jornada_Identidade_Progressiva.sql");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(runner, Does.Contain("THROW 51835"));
            Assert.That(runner, Does.Contain("r.melhor_candidato_uuid IS NOT NULL"));
            Assert.That(runner, Does.Contain("r.segundo_candidato_uuid IS NOT NULL"));
            Assert.That(runner, Does.Contain("r.pessoa_uuid_resolvido IS NOT NULL"));
            Assert.That(runner, Does.Contain("c.raw_motivo LIKE N'SEM[_]CANDIDATO[_]%'"));
            Assert.That(runner, Does.Contain("c.pessoa_origem_id IS NOT NULL"));
            Assert.That(runner, Does.Contain("c.initial_uuid IS NOT NULL"));
            Assert.That(runner, Does.Contain("THEN N'NOVA_IDENTIDADE'"));
            Assert.That(runner, Does.Contain("ELSE N'INDEFINIDA'"));
            Assert.That(runner, Does.Contain("THEN c.initial_uuid"));
            Assert.That(sql, Does.Contain("resultado='INDEFINIDA' AND estado='INDEFINIDA' AND canonical_uuid IS NULL"));
            Assert.That(sql, Does.Contain("resultado='NOVA_IDENTIDADE' AND estado='REFERENCIA' AND canonical_uuid IS NOT NULL"));
        }));
    }

    [Test]
    public void Gold_publication_reuses_existing_batch_recomposition()
    {
        var runner = ReadRepositoryFile("src", "Jornada.Linkage.Runner", "ProbabilisticLinkageBatchRunner.cs");
        var publisher = ReadRepositoryFile("src", "Jornada.Linkage.Runner", "LinkagePublicationBatchSql.cs");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(runner, Does.Contain("LinkagePublicationBatchSql.RecomposeAffectedGoldSql"));
            Assert.That(runner, Does.Contain("LinkagePublicationBatchSql.RefreshServingAssignmentsSql"));
            Assert.That(publisher, Does.Contain("MERGE gold.pessoa WITH (HOLDLOCK)"));
            Assert.That(publisher, Does.Contain("identidade.pessoa_origem_progressiva"));
            Assert.That(publisher, Does.Contain("p.initial_uuid"));
        }));
    }
}
