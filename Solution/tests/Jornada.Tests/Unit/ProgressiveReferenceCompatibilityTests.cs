using Jornada.Contracts;
using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ProgressiveReferenceCompatibilityTests
{
    [TestCase("PROVISORIA", ProgressiveIdentityStatus.PROVISORIA)]
    [TestCase("RESOLVIDA", ProgressiveIdentityStatus.REFERENCIA)]
    [TestCase("REFERENCIA", ProgressiveIdentityStatus.REFERENCIA)]
    [TestCase("INDEFINIDA", ProgressiveIdentityStatus.INDEFINIDA)]
    public void HistoricalAndCurrentValuesHaveExplicitMappings(string value, ProgressiveIdentityStatus expected)
    {
        Assert.That(ProgressiveIdentityOriginStore.ParseStatus(value), Is.EqualTo(expected));
    }

    [TestCase("RESOLVIDO")]
    [TestCase("CONFLITO_IDENTIDADE")]
    [TestCase("REFERENCIA ")]
    [TestCase("")]
    public void UnrelatedOrMalformedStatesFailClosed(string value)
    {
        Assert.Throws<InvalidOperationException>(() => ProgressiveIdentityOriginStore.ParseStatus(value));
    }

    [Test]
    public void ReferenceDoesNotChangeResolutionOutcomes()
    {
        Assert.That(Enum.GetNames<ProgressiveResolutionOutcome>(), Is.EquivalentTo(new[] { "NOVA_IDENTIDADE", "ASSOCIACAO_EXISTENTE", "INDEFINIDA" }));
    }
}
