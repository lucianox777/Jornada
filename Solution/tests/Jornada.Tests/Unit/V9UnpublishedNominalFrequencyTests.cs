using Jornada.Linkage.Runner;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class V9UnpublishedNominalFrequencyTests
{
    [Test]
    public void MissingTerm_UsesTwentyOverConfiguredPopulation()
    {
        var values = new Dictionary<string, decimal> { ["PUBLICADO"] = 0.01m };
        Assert.That(V9UnpublishedNominalFrequency.Resolve(values, "INEDITO", 11000000),
            Is.EqualTo(20m / 11000000m));
        Assert.That(V9UnpublishedNominalFrequency.Resolve(values, "INEDITO"),
            Is.EqualTo(20m / 11911337m));
    }

    [Test]
    public void PublishedTerm_RemainsUnmodified()
    {
        var values = new Dictionary<string, decimal> { ["PUBLICADO"] = 0.0000001m };
        Assert.That(V9UnpublishedNominalFrequency.Resolve(values, "PUBLICADO"),
            Is.EqualTo(0.0000001m));
    }

    [TestCase(0L)]
    [TestCase(19L)]
    public void InvalidPopulation_IsRejected(long population)
    {
        Assert.Throws<InvalidDataException>(() =>
            V9UnpublishedNominalFrequency.Resolve(new Dictionary<string, decimal>(),
                "INEDITO", population));
    }
}
