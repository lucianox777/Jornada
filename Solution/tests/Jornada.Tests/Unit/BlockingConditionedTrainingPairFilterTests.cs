using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class BlockingConditionedTrainingPairFilterTests
{
    [Test]
    public void Retains_when_any_complete_pass_agrees()
    {
        var observation = new BlockingFeatureObservation(
            false,
            new Dictionary<string, bool?>(StringComparer.Ordinal)
            {
                ["birth_year"] = true,
                ["name_first"] = false,
                ["mother_name_first"] = true
            });
        var passes = new[]
        {
            LinkageBlockingPass.Create("P1", new[] { "birth_year", "name_first" }),
            LinkageBlockingPass.Create("P2", new[] { "birth_year", "mother_name_first" })
        };

        Assert.That(BlockingConditionedTrainingPairFilter.Retains(observation, passes), Is.True);
    }

    [Test]
    public void Rejects_when_field_is_missing_or_disagrees()
    {
        var observation = new BlockingFeatureObservation(
            false,
            new Dictionary<string, bool?>(StringComparer.Ordinal)
            {
                ["birth_year"] = true,
                ["name_first"] = false
            });
        var passes = new[]
        {
            LinkageBlockingPass.Create("P1", new[] { "birth_year", "name_first" }),
            LinkageBlockingPass.Create("P2", new[] { "birth_year", "mother_name_first" })
        };

        Assert.That(BlockingConditionedTrainingPairFilter.Retains(observation, passes), Is.False);
    }
}
