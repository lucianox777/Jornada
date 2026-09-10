using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

[TestFixture]
public sealed class BlockingRuleSetOptimizerTests
{
    [Test]
    public void SelectBest_PrioritizesRecallBeforeStructuralCost()
    {
        var observations = new[]
        {
            Obs(true, ("a", true), ("b", true)),
            Obs(true, ("a", false), ("b", true)),
            Obs(false, ("a", false), ("b", false)),
            Obs(false, ("a", false), ("b", true))
        };
        var cheap = new[] { Pass("cheap", "a") };
        var higherRecall = new[] { Pass("p1", "a"), Pass("p2", "b") };

        var best = BlockingRuleSetOptimizer.SelectBest(
            observations,
            new IReadOnlyList<LinkageBlockingPass>[] { cheap, higherRecall });

        Assert.That(best.CanonicalSignature, Is.EqualTo("p1:a||p2:b"));
        Assert.That(best.Diagnostic.TrueMatchRecall, Is.EqualTo(1d));
    }

    [Test]
    public void SelectBest_PrioritizesReductionWhenRecallTies()
    {
        var observations = new[]
        {
            Obs(true, ("a", true), ("b", true)),
            Obs(true, ("a", true), ("b", true)),
            Obs(false, ("a", true), ("b", false)),
            Obs(false, ("a", false), ("b", false))
        };

        var broad = new[] { Pass("broad", "a") };
        var selective = new[] { Pass("selective", "a", "b") };
        var best = BlockingRuleSetOptimizer.SelectBest(
            observations,
            new IReadOnlyList<LinkageBlockingPass>[] { broad, selective });

        Assert.That(best.CanonicalSignature, Is.EqualTo("selective:a+b"));
        Assert.That(best.Diagnostic.ReductionRatio, Is.EqualTo(1d));
    }

    [Test]
    public void SelectBest_UsesLowerComplexityOnlyAfterMetricsTie()
    {
        var observations = new[]
        {
            Obs(true, ("a", true), ("b", true)),
            Obs(false, ("a", false), ("b", false))
        };
        var simple = new[] { Pass("simple", "a") };
        var complex = new[] { Pass("complex", "a", "b") };

        var best = BlockingRuleSetOptimizer.SelectBest(
            observations,
            new IReadOnlyList<LinkageBlockingPass>[] { complex, simple });

        Assert.That(best.CanonicalSignature, Is.EqualTo("simple:a"));
        Assert.That(best.FieldClauseCount, Is.EqualTo(1));
    }

    [Test]
    public void SelectBest_FailsClosedWhenMinimumRecallIsNotMet()
    {
        var observations = new[]
        {
            Obs(true, ("a", false)),
            Obs(false, ("a", false))
        };

        Assert.Throws<InvalidOperationException>(() =>
            BlockingRuleSetOptimizer.SelectBest(
                observations,
                new IReadOnlyList<LinkageBlockingPass>[] { new[] { Pass("a", "a") } },
                minimumTrueMatchRecall: 0.95d));
    }

    private static LinkageBlockingPass Pass(string id, params string[] fields) =>
        LinkageBlockingPass.Create(id, fields);

    private static BlockingFeatureObservation Obs(
        bool match,
        params (string Field, bool? Agreement)[] values) =>
        new(match, values.ToDictionary(static x => x.Field, static x => x.Agreement, StringComparer.Ordinal));
}
