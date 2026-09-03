using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class ProbabilisticLinkageVersioningTests
{
    [Test]
    public void Decision_keeps_model_and_only_top_two_candidates()
    {
        var modelId = Guid.NewGuid();
        var best = Guid.NewGuid();
        var second = Guid.NewGuid();
        var decision = new ProbabilisticLinkageDecision(
            ResolutionStatus.CONFLITO,
            null,
            best,
            0.94m,
            second,
            0.93m,
            0.01m,
            modelId,
            "MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE");

        Assert.Multiple(() =>
        {
            Assert.That(decision.ModeloId, Is.EqualTo(modelId));
            Assert.That(decision.MelhorCandidatoUuid, Is.EqualTo(best));
            Assert.That(decision.SegundoCandidatoUuid, Is.EqualTo(second));
            Assert.That(decision.Margem, Is.EqualTo(0.01m));
            Assert.That(decision.PessoaUuidResolvido, Is.Null);
        });
    }

    [Test]
    public void Run_summary_uses_long_counts_for_million_scale()
    {
        var summary = new ProbabilisticLinkageRunSummary(
            Guid.NewGuid(), Guid.NewGuid(), 12, LinkageRunStatus.PUBLICADO,
            25_000_000L, 25_000_000L, 24_000_000L, 800_000L, 200_000L, 0L,
            DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Assert.That(summary.Avaliados, Is.EqualTo(25_000_000L));
    }
}
