using Jornada.Linkage.Runner;
using Jornada.Linkage.Parameters.Worker;
using System.Text.Json;
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
    public void MunicipalImputation_UsesTenOccurrencesWithoutChangingPublishedFrequency()
    {
        var published = new Dictionary<string, decimal> { ["SILVA"] = 0.08m };
        Assert.That(V9UnpublishedNominalFrequency.Resolve(published, "INEDITO",
            11911337L, 10L), Is.EqualTo(10m / 11911337m));
        Assert.That(V9UnpublishedNominalFrequency.Resolve(published, "SILVA",
            11911337L, 10L), Is.EqualTo(0.08m));
    }

    [TestCase(0L, 100L)]
    [TestCase(-1L, 100L)]
    [TestCase(101L, 100L)]
    public void InvalidOccurrenceCount_IsRejected(long count, long population)
    {
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9UnpublishedNominalFrequency.Resolve(new Dictionary<string, decimal>(),
                "INEDITO", population, count)));
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
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9UnpublishedNominalFrequency.Resolve(new Dictionary<string, decimal>(),
                "INEDITO", population)));
    }
    [Test]
    public void PhysicalModelOptionJson_OverridesContextPopulationAndChangesFingerprint()
    {
        const string json = """
            {"OptionId":"TF_CONTEXT","SmoothingAlpha":0.5,"DecisionThreshold":0.9,
             "ContextPopulation":12000000,"UnpublishedOccurrenceCount":10}
            """;
        var configured = JsonSerializer.Deserialize<V9FsAdaptiveOptionConfiguration>(json)!;
        var baseline = configured with
        {
            ContextPopulation = V9UnpublishedNominalFrequency.DefaultContextPopulation
        };
        Assert.Multiple((Action)(() =>
        {
            Assert.That(configured.ContextPopulation, Is.EqualTo(12000000L));
            Assert.That(configured.UnpublishedOccurrenceCount, Is.EqualTo(10L));
            Assert.That(configured.FingerprintSha256, Is.Not.EqualTo(baseline.FingerprintSha256));
            Assert.That(V9UnpublishedNominalFrequency.Resolve(
                new Dictionary<string, decimal>(), "INEDITO", configured.ContextPopulation,
                configured.UnpublishedOccurrenceCount),
                Is.EqualTo(10m / 12000000m));
        }));
    }

    [Test]
    public void PhysicalModelOptionJson_DefaultsPopulationWhenAbsent()
    {
        const string json = """
            {"OptionId":"TF_DEFAULT","SmoothingAlpha":0.5,"DecisionThreshold":0.9}
            """;
        var configured = JsonSerializer.Deserialize<V9FsAdaptiveOptionConfiguration>(json)!;
        Assert.That(configured.ContextPopulation,
            Is.EqualTo(V9UnpublishedNominalFrequency.DefaultContextPopulation));
    }
}
