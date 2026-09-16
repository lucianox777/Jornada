using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

[TestFixture]
public sealed class BlockingRuleSetOptimizerTests
{
    [Test]
    public void SelectBest_MaximizesReductionAfterMinimumRecallIsMet()
    {
        var observations = new[]
        {
            Obs(true, ("broad", true), ("selective", true)),
            Obs(true, ("broad", true), ("selective", false)),
            Obs(false, ("broad", true), ("selective", true)),
            Obs(false, ("broad", true), ("selective", false)),
            Obs(false, ("broad", true), ("selective", false)),
            Obs(false, ("broad", true), ("selective", false))
        };
        var broad = new[] { Pass("broad", "broad") };
        var selective = new[] { Pass("selective", "selective") };

        var best = BlockingRuleSetOptimizer.SelectBest(
            observations,
            new IReadOnlyList<LinkageBlockingPass>[] { broad, selective },
            minimumTrueMatchRecall: 0.5d,
            requireObservedNonMatchSupport: true);

        Assert.Multiple(() =>
        {
            Assert.That(best.CanonicalSignature, Is.EqualTo("selective:selective"));
            Assert.That(best.Diagnostic.TrueMatchRecall, Is.EqualTo(0.5d));
            Assert.That(best.Diagnostic.ReductionRatio, Is.EqualTo(0.75d));
        });
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
    public void SelectBest_UsesHigherRecallOnlyAfterReductionTies()
    {
        var observations = new[]
        {
            Obs(true, ("a", true), ("b", true)),
            Obs(true, ("a", false), ("b", true)),
            Obs(false, ("a", false), ("b", false)),
            Obs(false, ("a", false), ("b", false))
        };
        var lowerRecall = new[] { Pass("lower", "a") };
        var higherRecall = new[] { Pass("higher", "b") };

        var best = BlockingRuleSetOptimizer.SelectBest(
            observations,
            new IReadOnlyList<LinkageBlockingPass>[] { lowerRecall, higherRecall },
            minimumTrueMatchRecall: 0.5d);

        Assert.Multiple(() =>
        {
            Assert.That(best.CanonicalSignature, Is.EqualTo("higher:b"));
            Assert.That(best.Diagnostic.ReductionRatio, Is.EqualTo(1d));
            Assert.That(best.Diagnostic.TrueMatchRecall, Is.EqualTo(1d));
        });
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
