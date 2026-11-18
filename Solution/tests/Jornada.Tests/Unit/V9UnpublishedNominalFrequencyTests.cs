using Jornada.Linkage.Runner;
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
    [Test]
    public void PhysicalModelOptionJson_OverridesContextPopulationAndChangesFingerprint()
    {
        const string json = """
            {"OptionId":"TF_CONTEXT","SmoothingAlpha":0.5,"DecisionThreshold":0.9,
             "ContextPopulation":12000000}
            """;
        var configured = JsonSerializer.Deserialize<V9FsAdaptiveOptionConfiguration>(json)!;
        var baseline = configured with
        {
            ContextPopulation = V9UnpublishedNominalFrequency.DefaultContextPopulation
        };
        Assert.Multiple(() =>
        {
            Assert.That(configured.ContextPopulation, Is.EqualTo(12000000L));
            Assert.That(configured.FingerprintSha256, Is.Not.EqualTo(baseline.FingerprintSha256));
            Assert.That(V9UnpublishedNominalFrequency.Resolve(
                new Dictionary<string, decimal>(), "INEDITO", configured.ContextPopulation),
                Is.EqualTo(20m / 12000000m));
        });
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
