using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ProbabilisticLinkageNoCandidateReasonTests
{
    [TestCase("SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO")]
    [TestCase("SEM_CANDIDATO_NOS_BLOCOS_NASCIMENTO_COMPONENTE")]
    [TestCase("SEM_CANDIDATO_RULESET")]
    public void No_candidate_reasons_are_counted_independently_of_blocking_generation(string reason)
    {
        Assert.That(ProbabilisticLinkageBatchRunner.IsNoCandidateReason(reason), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("ABAIXO_T_LINKAGE")]
    [TestCase("MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE")]
    public void Scored_decisions_are_not_counted_as_no_candidate(string? reason)
    {
        Assert.That(ProbabilisticLinkageBatchRunner.IsNoCandidateReason(reason), Is.False);
    }
}
