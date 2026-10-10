using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt05HistoricalCandidateStateVerifierContractTests
{
    [Test]
    public void Verifier_is_fail_closed_before_historical_candidate_consumption()
    {
        var root = TestContext.CurrentContext.TestDirectory;
        var path = Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", "..", "src",
            "Jornada.Linkage.Runner", "Dt05HistoricalCandidateStateVerifier.cs"));
        var source = File.ReadAllText(path);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(source, Does.Contain("schema_version").And.Contain("!= 2"));
            Assert.That(source, Does.Contain("snapshot_kind").And.Contain("candidate-state"));
            Assert.That(source, Does.Contain("run_id").And.Contain("binding.SourceRunId"));
            Assert.That(source, Does.Contain("binding.ManifestSha256"));
            Assert.That(source, Does.Contain("binding.PartitionSetSha256"));
            Assert.That(source, Does.Contain("binding.CandidateReferenceCount"));
            Assert.That(source, Does.Contain("logical_sha256"));
            Assert.That(source, Does.Contain("bytes.LongLength != expectedBytes"));
            Assert.That(source, Does.Contain("objects/{expectedSha[..2]}/{expectedSha}.parquet"));
            Assert.That(source, Does.Contain("partitionRows != rowCount"));
            Assert.That(source, Does.Not.Contain("gold.pessoa"));
        }));
    }
}
