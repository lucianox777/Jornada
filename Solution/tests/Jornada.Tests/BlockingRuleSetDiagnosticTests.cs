using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class BlockingRuleSetDiagnosticTests
{
    [Test]
    public void Analyze_UsesAndWithinPassAndOrAcrossPasses()
    {
        var observations = new[]
        {
            Obs(true,  ("name_first", true),  ("birth_year", true),  ("mother_name_first", false)),
            Obs(true,  ("name_first", false), ("birth_year", true),  ("mother_name_first", true)),
            Obs(false, ("name_first", true),  ("birth_year", false), ("mother_name_first", false)),
            Obs(false, ("name_first", true),  ("birth_year", true),  ("mother_name_first", false))
        };
        var passes = new[]
        {
            LinkageBlockingPass.Create("person-year", new[] { "name_first", "birth_year" }),
            LinkageBlockingPass.Create("mother-year", new[] { "mother_name_first", "birth_year" })
        };

        var result = BlockingRuleSetDiagnostic.Analyze(observations, passes);

        Assert.Multiple(() =>
        {
            Assert.That(result.TrueMatchRecall, Is.EqualTo(1d));
            Assert.That(result.NonMatchRetention, Is.EqualTo(0.5d));
            Assert.That(result.ReductionRatio, Is.EqualTo(0.5d));
        });
    }

    [Test]
    public void Analyze_DoesNotWeakenIncompletePass()
    {
        var observations = new[]
        {
            Obs(true,  ("name_first", true), ("birth_year", null)),
            Obs(false, ("name_first", true), ("birth_year", true))
        };
        var passes = new[]
        {
            LinkageBlockingPass.Create("name-year", new[] { "name_first", "birth_year" })
        };

        var result = BlockingRuleSetDiagnostic.Analyze(observations, passes);

        Assert.Multiple(() =>
        {
            Assert.That(result.TrueMatchRecall, Is.EqualTo(0d));
            Assert.That(result.CompleteMatchCoverage, Is.EqualTo(0d));
            Assert.That(result.NonMatchRetention, Is.EqualTo(1d));
        });
    }

    [Test]
    public void Analyze_HonorsWeights()
    {
        var observations = new[]
        {
            Obs(true, 2m, ("name_first", true)),
            Obs(true, 1m, ("name_first", false)),
            Obs(false, 3m, ("name_first", false)),
            Obs(false, 1m, ("name_first", true))
        };
        var passes = new[]
        {
            LinkageBlockingPass.Create("name", new[] { "name_first" })
        };

        var result = BlockingRuleSetDiagnostic.Analyze(observations, passes);

        Assert.Multiple(() =>
        {
            Assert.That(result.TrueMatchRecall, Is.EqualTo(2d / 3d).Within(1e-12));
            Assert.That(result.NonMatchRetention, Is.EqualTo(0.25d).Within(1e-12));
            Assert.That(result.ReductionRatio, Is.EqualTo(0.75d).Within(1e-12));
        });
    }

    private static BlockingFeatureObservation Obs(
        bool match,
        params (string Field, bool? Agreement)[] agreements) =>
        Obs(match, 1m, agreements);

    private static BlockingFeatureObservation Obs(
        bool match,
        decimal weight,
        params (string Field, bool? Agreement)[] agreements) =>
        new(match, agreements.ToDictionary(static x => x.Field, static x => x.Agreement, StringComparer.Ordinal), weight);
}
