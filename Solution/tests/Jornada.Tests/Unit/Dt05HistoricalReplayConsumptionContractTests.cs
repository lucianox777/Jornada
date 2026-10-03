using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt05HistoricalReplayConsumptionContractTests
{
    private static string Root()
    {
        var dir=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while(dir is not null && !Directory.Exists(Path.Combine(dir.FullName,"database","migrations"))) dir=dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }

    [Test]
    public void Replay_requires_exact_source_binding_and_verified_candidate_state()
    {
        var root=Root();
        var runner=File.ReadAllText(Path.Combine(root,"src","Jornada.Linkage.Runner","ProbabilisticLinkageBatchRunner.cs"));
        var linkage=File.ReadAllText(Path.Combine(root,"src","Jornada.Linkage.Runner","SqlProbabilisticIdentityLinkage.cs"));
        Assert.Multiple(() => {
            Assert.That(runner,Does.Contain("ReadHistoricalCandidateStateBindingAsync"));
            Assert.That(runner,Does.Contain("historicalCandidateStateVerifier.VerifyAsync"));
            Assert.That(runner,Does.Contain("UseHistoricalCandidates"));
            Assert.That(linkage,Does.Contain("REPLAY sem candidate-state histórico verificado; fallback para Gold recusado."));
            Assert.That(runner,Does.Contain("ReadHistoricalBlockingProjectionBindingAsync"));
            Assert.That(runner,Does.Contain("historicalBlockingProjectionVerifier.VerifyAsync"));
            Assert.That(linkage,Does.Contain("REPLAY dinâmico sem blocking-projection histórica verificada; fallback SQL recusado."));
        });
    }

    [Test]
    public void Replay_source_is_propagated_from_cli_to_batch_contract()
    {
        var root=Root();
        var worker=File.ReadAllText(Path.Combine(root,"src","Jornada.Linkage.Runner","LinkageRunnerWorker.cs"));
        var contracts=File.ReadAllText(Path.Combine(root,"src","Jornada.Contracts","ProbabilisticLinkageContracts.cs"));
        Assert.That(worker,Does.Contain("options.ReplaySourceRunId"));
        Assert.That(contracts,Does.Contain("Guid? ReplaySourceRunId = null"));
    }
}
