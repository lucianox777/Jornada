namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt05CandidateStateSqlBindingContractTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "database", "migrations"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Solution root");
    }

    [Test]
    public void Candidate_state_publication_is_bound_create_once_with_manifest_v3()
    {
        var root = Root();
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "20261002_Linkage_Replay_Candidate_State_Binding_DT05.sql"));
        var manifest = File.ReadAllText(Path.Combine(root, "database", "migrations", "manifest.txt"));
        var sql = File.ReadAllText(Path.Combine(root, "src", "Jornada.Linkage.Runner", "Dt05ReplaySql.cs"));
        var runner = File.ReadAllText(Path.Combine(root, "src", "Jornada.Linkage.Runner", "ProbabilisticLinkageBatchRunner.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(manifest, Does.Contain("migrations/20261002_Linkage_Replay_Candidate_State_Binding_DT05.sql"));
            Assert.That(migration, Does.Contain("candidate_state_caminho_logico"));
            Assert.That(migration, Does.Contain("candidate_state_manifesto_sha256"));
            Assert.That(migration, Does.Contain("candidate_state_partition_set_sha256"));
            Assert.That(migration, Does.Contain("linkage-snapshots/v1/candidate-state/manifests/%"));
            Assert.That(migration, Does.Contain("schema v3 exige binding físico válido do candidate-state"));
            Assert.That(sql, Does.Contain("Dt05CandidateStateSnapshot candidateState"));
            Assert.That(sql, Does.Contain("candidateState.ManifestLogicalPath"));
            Assert.That(sql, Does.Contain("candidateState.ManifestSha256"));
            Assert.That(sql, Does.Contain("candidateState.PartitionSetSha256"));
            Assert.That(runner, Does.Contain("identity, candidateState, blockingProjection, workCt"));
        });
    }
}
