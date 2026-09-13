using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class NameFrequencyStratificationTests
{
    [Test]
    public void Without_versioned_boundaries_frequency_remains_unknown()
    {
        var result = NameFrequencyStratification.Classify(
            new Dictionary<string, decimal>(), "NOME", 0.00001m);

        Assert.That(result, Is.EqualTo(NameFrequencyStratum.UNKNOWN));
    }

    [TestCase("NOME", 0.00005, NameFrequencyStratum.RARE)]
    [TestCase("NOME", 0.00050, NameFrequencyStratum.UNCOMMON)]
    [TestCase("NOME", 0.00500, NameFrequencyStratum.COMMON)]
    [TestCase("NOME", 0.05000, NameFrequencyStratum.VERY_COMMON)]
    [TestCase("NOME_MAE", 0.00005, NameFrequencyStratum.RARE)]
    public void Uses_only_boundaries_persisted_in_model(
        string attribute, double probability, NameFrequencyStratum expected)
    {
        var p = new Dictionary<string, decimal>
        {
            [$"FREQ_{attribute}_RARE_MAX_PROBABILITY"] = 0.0001m,
            [$"FREQ_{attribute}_UNCOMMON_MAX_PROBABILITY"] = 0.001m,
            [$"FREQ_{attribute}_COMMON_MAX_PROBABILITY"] = 0.01m
        };

        Assert.That(NameFrequencyStratification.Classify(p, attribute, (decimal)probability), Is.EqualTo(expected));
    }

    [Test]
    public void Publication_probability_uses_first_name_semantics_and_never_last_token()
    {
        var frequency = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["MARIA"] = 0.10m,
            ["SILVA"] = 0.20m
        };

        var result = NameFrequencyStratification.ResolvePublicationKeyProbability(
            frequency, "Maria Clara da Silva");

        Assert.That(result, Is.EqualTo(0.10m));
    }

    [Test]
    public void Invalid_boundaries_fail_closed()
    {
        var p = new Dictionary<string, decimal>
        {
            ["FREQ_NOME_RARE_MAX_PROBABILITY"] = 0.01m,
            ["FREQ_NOME_UNCOMMON_MAX_PROBABILITY"] = 0.001m,
            ["FREQ_NOME_COMMON_MAX_PROBABILITY"] = 0.10m
        };

        Assert.Throws<InvalidOperationException>(() =>
            NameFrequencyStratification.Classify(p, "NOME", 0.005m));
    }
}
