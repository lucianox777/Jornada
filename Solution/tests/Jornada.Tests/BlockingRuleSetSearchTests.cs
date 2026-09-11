using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

[TestFixture]
public sealed class BlockingRuleSetSearchTests
{
    [Test]
    public void SearchBest_CanSelectTwoComplementaryPasses()
    {
        var observations = new[]
        {
            Obs(true, ("a", true), ("b", false)),
            Obs(true, ("a", false), ("b", true)),
            Obs(false, ("a", false), ("b", false)),
            Obs(false, ("a", false), ("b", false))
        };

        var best = BlockingRuleSetSearch.SearchBest(
            observations,
            new[] { "a", "b" },
            new BlockingRuleSetSearchOptions(
                MaxFieldsPerPass: 1,
                MaxPasses: 2,
                PrimitivePoolSize: 2,
                MinimumTrueMatchRecall: 1d));

        Assert.Multiple(() =>
        {
            Assert.That(best.Passes.Count, Is.EqualTo(2));
            Assert.That(best.Diagnostic.TrueMatchRecall, Is.EqualTo(1d));
            Assert.That(best.Diagnostic.ReductionRatio, Is.EqualTo(1d));
        });
    }

    [Test]
    public void SearchBest_CanSelectIntersectionOfDerivedNameAndBirthFeature()
    {
        var name = BlockingCandidateFeatureCatalog.FullNameWithoutParticles;
        var year = BlockingCandidateFeatureCatalog.BirthYear;
        var observations = new[]
        {
            Obs(true, (name, true), (year, true)),
            Obs(true, (name, true), (year, true)),
            Obs(false, (name, true), (year, false)),
            Obs(false, (name, true), (year, false)),
            Obs(false, (name, false), (year, true)),
            Obs(false, (name, false), (year, true))
        };

        var best = BlockingRuleSetSearch.SearchBest(
            observations,
            new[] { name, year },
            new BlockingRuleSetSearchOptions(
                MaxFieldsPerPass: 2,
                MaxPasses: 1,
                PrimitivePoolSize: 3,
                MinimumTrueMatchRecall: 1d));

        Assert.Multiple(() =>
        {
            Assert.That(best.Passes, Has.Count.EqualTo(1));
            Assert.That(best.Passes[0].Fields, Is.EquivalentTo(new[] { name, year }));
            Assert.That(best.Diagnostic.TrueMatchRecall, Is.EqualTo(1d));
            Assert.That(best.Diagnostic.ReductionRatio, Is.EqualTo(1d));
        });
    }

    [Test]
    public void SearchBest_DoesNotKeepRedundantIntersectionWhenMetricsAreIdentical()
    {
        var normalized = BlockingCandidateFeatureCatalog.FullName;
        var noDiacritics = BlockingCandidateFeatureCatalog.FullNameUpperNoDiacritics;
        var observations = new[]
        {
            Obs(true, (normalized, true), (noDiacritics, true)),
            Obs(true, (normalized, true), (noDiacritics, true)),
            Obs(false, (normalized, false), (noDiacritics, false)),
            Obs(false, (normalized, false), (noDiacritics, false))
        };

        var best = BlockingRuleSetSearch.SearchBest(
            observations,
            new[] { normalized, noDiacritics },
            new BlockingRuleSetSearchOptions(
                MaxFieldsPerPass: 2,
                MaxPasses: 1,
                PrimitivePoolSize: 3,
                MinimumTrueMatchRecall: 1d));

        Assert.Multiple(() =>
        {
            Assert.That(best.Passes, Has.Count.EqualTo(1));
            Assert.That(best.Passes[0].Fields, Has.Count.EqualTo(1));
            Assert.That(best.Diagnostic.TrueMatchRecall, Is.EqualTo(1d));
            Assert.That(best.Diagnostic.ReductionRatio, Is.EqualTo(1d));
        });
    }

    [Test]
    public void SearchBest_IsDeterministicWhenFeatureInputOrderChanges()
    {
        var observations = new[]
        {
            Obs(true, ("a", true), ("b", true), ("c", true)),
            Obs(false, ("a", true), ("b", false), ("c", false))
        };
        var options = new BlockingRuleSetSearchOptions(2, 2, 4, 0.9d);

        var first = BlockingRuleSetSearch.SearchBest(observations, new[] { "c", "a", "b" }, options);
        var second = BlockingRuleSetSearch.SearchBest(observations, new[] { "b", "c", "a" }, options);

        Assert.That(first.CanonicalSignature, Is.EqualTo(second.CanonicalSignature));
    }

    [Test]
    public void SearchOptions_RejectUnboundedValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BlockingRuleSetSearchOptions(MaxFieldsPerPass: 4).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BlockingRuleSetSearchOptions(MaxPasses: 3).Validate());
    }

    private static BlockingFeatureObservation Obs(
        bool match,
        params (string Field, bool? Agreement)[] values) =>
        new(match, values.ToDictionary(static x => x.Field, static x => x.Agreement, StringComparer.Ordinal));
}
