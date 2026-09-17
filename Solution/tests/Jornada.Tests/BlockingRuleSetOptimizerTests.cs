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
    public void SelectBest_PenalizesPassWithoutObservedIncrementalGainAfterPrimaryMetricsTie()
    {
        var observations = new[]
        {
            Obs(true, ("a", true), ("b", true), ("c", true), ("d", false)),
            Obs(true, ("a", true), ("b", false), ("c", false), ("d", true)),
            Obs(false, ("a", false), ("b", false), ("c", false), ("d", false)),
            Obs(false, ("a", false), ("b", false), ("c", false), ("d", false))
        };
        var redundant = new[] { Pass("a", "a"), Pass("b", "b") };
        var complementary = new[] { Pass("c", "c"), Pass("d", "d") };

        var best = BlockingRuleSetOptimizer.SelectBest(
            observations,
            new IReadOnlyList<LinkageBlockingPass>[] { redundant, complementary });

        Assert.Multiple(() =>
        {
            Assert.That(best.CanonicalSignature, Is.EqualTo("c:c||d:d"));
            Assert.That(best.UnjustifiedZeroGainPassCount, Is.Zero);
            Assert.That(best.PassContributions, Has.All.Matches<BlockingPassMarginalContribution>(
                contribution => contribution.IncrementalTrueMatchWeight > 0m));
        });
    }

    [Test]
    public void SelectBest_JustifiesZeroTruthGainWhenPassProvidesRequiredObservedNonMatchSupport()
    {
        var observations = new[]
        {
            Obs(true, ("truth", true), ("support", false)),
            Obs(true, ("truth", true), ("support", false)),
            Obs(false, ("truth", false), ("support", true)),
            Obs(false, ("truth", false), ("support", false))
        };
        var candidate = new[] { Pass("truth", "truth"), Pass("support", "support") };

        var best = BlockingRuleSetOptimizer.SelectBest(
            observations,
            new IReadOnlyList<LinkageBlockingPass>[] { candidate },
            minimumTrueMatchRecall: 1d,
            requireObservedNonMatchSupport: true);

        var support = best.PassContributions.Single(contribution => contribution.PassId == "support");
        Assert.Multiple(() =>
        {
            Assert.That(support.IncrementalTrueMatchWeight, Is.Zero);
            Assert.That(support.IncrementalNonMatchWeight, Is.EqualTo(1m));
            Assert.That(support.Justification, Is.EqualTo("OBSERVED_NONMATCH_SUPPORT"));
            Assert.That(best.UnjustifiedZeroGainPassCount, Is.Zero);
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
