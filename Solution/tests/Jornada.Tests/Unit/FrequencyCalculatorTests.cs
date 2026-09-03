using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class FrequencyCalculatorTests
{
    [Test]
    public void Calculates_population_frequency()
    {
        var result = FrequencyCalculator.Calculate(["SILVA", "SILVA", "SOUZA", "RARO"]);
        Assert.That(result["SILVA"].Occurrences, Is.EqualTo(2));
        Assert.That(result["SILVA"].Frequency, Is.EqualTo(0.5m));
        Assert.That(result["RARO"].Frequency, Is.EqualTo(0.25m));
    }
}
