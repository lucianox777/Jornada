using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class Dt05HistoricalReplaySourceContractTests
{
    [Test]
    public void Replay_sql_resolver_is_fail_closed_on_candidate_state_v3_or_v4_binding()
    {
        var root = TestContext.CurrentContext.TestDirectory;
        var path = Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", "..", "src", "Jornada.Linkage.Runner", "Dt05ReplaySql.cs"));
        var sql = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("ReadHistoricalCandidateStateBindingAsync"));
            Assert.That(sql, Does.Contain("m.schema_version IN (3,4)"));
            Assert.That(sql, Does.Contain("candidate_state_caminho_logico"));
            Assert.That(sql, Does.Contain("candidate_state_manifesto_sha256"));
            Assert.That(sql, Does.Contain("candidate_state_partition_set_sha256"));
            Assert.That(sql, Does.Contain("binding candidate-state v3/v4 completo; replay recusado"));
            Assert.That(sql, Does.Not.Contain("FROM gold.pessoa"));
        });
    }
}
