using Jornada.Linkage.Parameters.Worker;
using Microsoft.Extensions.Configuration;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class BlockingRuleSetSearchConfigurationTests
{
    [Test]
    public void Missing_configuration_preserves_current_algorithm_defaults()
    {
        var configuration = new ConfigurationBuilder().Build();
        var options = BlockingRuleSetSearchConfiguration.FromConfiguration(configuration);

        Assert.Multiple(() =>
        {
            Assert.That(options.MaxFieldsPerPass, Is.EqualTo(2));
            Assert.That(options.MaxPasses, Is.EqualTo(2));
            Assert.That(options.PrimitivePoolSize, Is.EqualTo(8));
            Assert.That(options.MinimumTrueMatchRecall, Is.EqualTo(0.95d));
        });
    }

    [Test]
    public void Explicit_configuration_is_parsed_without_inventing_new_values()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LinkageParameters:BlockingSearch:MaxFieldsPerPass"] = "3",
                ["LinkageParameters:BlockingSearch:MaxPasses"] = "1",
                ["LinkageParameters:BlockingSearch:PrimitivePoolSize"] = "16",
                ["LinkageParameters:BlockingSearch:MinimumTrueMatchRecall"] = "0.975"
            })
            .Build();

        var options = BlockingRuleSetSearchConfiguration.FromConfiguration(configuration);

        Assert.Multiple(() =>
        {
            Assert.That(options.MaxFieldsPerPass, Is.EqualTo(3));
            Assert.That(options.MaxPasses, Is.EqualTo(1));
            Assert.That(options.PrimitivePoolSize, Is.EqualTo(16));
            Assert.That(options.MinimumTrueMatchRecall, Is.EqualTo(0.975d));
        });
    }

    [Test]
    public void Invalid_configuration_fails_closed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LinkageParameters:BlockingSearch:MaxPasses"] = "3"
            })
            .Build();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BlockingRuleSetSearchConfiguration.FromConfiguration(configuration));
    }
}
