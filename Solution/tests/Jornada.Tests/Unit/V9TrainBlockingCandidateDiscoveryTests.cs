using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9TrainBlockingCandidateDiscoveryTests
{
    [Test]
    public void Prefixes_use_canonical_full_names_and_K_changes_effective_agreement()
    {
        var left = V9CalibrationBlockingProjection.Project("  Ána  Silva ", "Joáo Costa", null);
        var right = V9CalibrationBlockingProjection.Project("AnaX Silva", "João Lima", null);
        string Value(IReadOnlyList<BlockingProjectionKey> keys, string feature) =>
            keys.Single(x => x.Feature == feature).Value;
        Assert.Multiple((Action)(() =>
        {
            Assert.That(Value(left, "name_prefix_3"), Is.EqualTo(Value(right, "name_prefix_3")));
            Assert.That(Value(left, "name_prefix_4"), Is.EqualTo("ANA "));
            Assert.That(Value(left, "name_prefix_4"), Is.Not.EqualTo(Value(right, "name_prefix_4")));
            Assert.That(Value(left, "mother_name_prefix_4"), Is.EqualTo("JOAO"));
            Assert.That(left.Where(x => !x.Feature.Contains("prefix_", StringComparison.Ordinal)),
                Is.EqualTo(BlockingProjectionKeyProjector.Project("  Ána  Silva ", "Joáo Costa", null)));
        }));
    }

    [Test]
    public void Missing_and_short_names_emit_no_wildcard_or_shortened_K_key()
    {
        var keys = V9CalibrationBlockingProjection.Project(" Li ", "  ", null);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(keys.Single(x => x.Feature == "name_prefix_2").Value, Is.EqualTo("LI"));
            Assert.That(keys.Any(x => x.Feature == "name_prefix_3"), Is.False);
            Assert.That(keys.Any(x => x.Feature.StartsWith("mother_name_", StringComparison.Ordinal)), Is.False);
            Assert.That(keys.Any(x => string.IsNullOrEmpty(x.Value)), Is.False);
            Assert.That(V9CalibrationBlockingProjection.Project(null, null, null), Is.Empty);
        }));
    }

    [Test]
    public void Prefix_schema_is_explicit_versioned_and_does_not_extend_global_legacy_catalog()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That(V9CalibrationBlockingProjection.SupportedFeatures(), Does.Contain("name_prefix_4"));
            Assert.That(V9CalibrationBlockingProjection.SupportedFeatures(5), Does.Contain("mother_name_prefix_5"));
            Assert.That(V9CalibrationBlockingProjection.ComputeSchemaFingerprint(4),
                Is.Not.EqualTo(V9CalibrationBlockingProjection.ComputeSchemaFingerprint(5)));
            Assert.That(BlockingCandidateFeatureCatalog.RequiredCalibratorCandidates,
                Does.Not.Contain("name_prefix_1"));
        }));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => V9CalibrationBlockingProjection.SupportedFeatures(65)));
    }

    [TestCase("VALIDATION")]
    [TestCase("TEST")]
    public void Discovery_rejects_non_TRAIN_before_proposal_generation(string partition)
    {
        var rows = Corpus();
        rows[0] = rows[0] with { Partition = partition };
        Assert.Throws<InvalidDataException>((Action)(() => V9TrainBlockingCandidateDiscovery.Discover(rows, SmallOptions())));
    }

    [Test]
    public void Discovery_includes_every_preblocking_positive_and_returns_all_retained_unions()
    {
        var result = V9TrainBlockingCandidateDiscovery.Discover(Corpus(), SmallOptions());
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.EligiblePositivePairsBeforeBlocking, Is.EqualTo(2));
            Assert.That(result.EligibleNonMatchPairsBeforeBlocking, Is.EqualTo(4));
            Assert.That(result.SampledNonMatchPairs, Is.EqualTo(4));
            Assert.That(result.TrainObservationCount, Is.EqualTo(4));
            Assert.That(result.TrainBaseIdentityCount, Is.EqualTo(2));
            Assert.That(result.EvaluatedPrimitiveCount, Is.EqualTo(27));
            Assert.That(result.RetainedPrimitiveCount, Is.EqualTo(4));
            Assert.That(result.EnumeratedUnionCount, Is.EqualTo(10));
            Assert.That(result.Candidates, Has.Count.EqualTo(10));
            Assert.That(result.PairFeatureComparisonCount, Is.EqualTo(6 * 27));
            Assert.That(result.ExhaustivePrimitivePool, Is.False);
            Assert.That(result.ProjectionSchemaFingerprintSha256,
                Is.EqualTo(V9CalibrationBlockingProjection.ComputeSchemaFingerprint()));
        }));
    }

    [Test]
    public void Discovery_is_deterministic_under_input_reordering()
    {
        var first = V9TrainBlockingCandidateDiscovery.Discover(Corpus(), SmallOptions());
        var second = V9TrainBlockingCandidateDiscovery.Discover(Corpus().Reverse().ToArray(), SmallOptions());
        Assert.Multiple((Action)(() =>
        {
            Assert.That(second.SamplePairFingerprintSha256, Is.EqualTo(first.SamplePairFingerprintSha256));
            Assert.That(second.TrainProjectionFingerprintSha256, Is.EqualTo(first.TrainProjectionFingerprintSha256));
            Assert.That(Signatures(second), Is.EqualTo(Signatures(first)));
        }));
    }

    [Test]
    public void Bounded_random_nonmatch_sample_is_reproducible_and_projection_content_is_pinned()
    {
        var rows = Enumerable.Range(1, 30).Select(i => new V9PhysicalCalibrationObservation(
            i, "person" + (i / 2), "TRAIN", "gestor" + i,
            "Name " + i, null, null)).ToArray();
        var options = SmallOptions() with { NonMatchSampleSize = 5 };
        var first = V9TrainBlockingCandidateDiscovery.Discover(rows, options);
        var reversed = V9TrainBlockingCandidateDiscovery.Discover(rows.Reverse().ToArray(), options);
        rows[0] = rows[0] with { PersonName = "Changed Name" };
        var changed = V9TrainBlockingCandidateDiscovery.Discover(rows, options);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(first.SampledNonMatchPairs, Is.EqualTo(5));
            Assert.That(reversed.SamplePairFingerprintSha256, Is.EqualTo(first.SamplePairFingerprintSha256));
            Assert.That(reversed.TrainProjectionFingerprintSha256, Is.EqualTo(first.TrainProjectionFingerprintSha256));
            Assert.That(changed.TrainProjectionFingerprintSha256, Is.Not.EqualTo(first.TrainProjectionFingerprintSha256));
        }));
    }

    [Test]
    public void Complete_positive_budget_failure_never_truncates_missed_pairs()
    {
        Assert.Throws<InvalidOperationException>((Action)(() =>
            V9TrainBlockingCandidateDiscovery.Discover(Corpus(), SmallOptions() with { PositivePairCap = 1 })));
    }

    [Test]
    public void Incomplete_explicit_nonmatch_workload_fails_instead_of_silently_sampling_less()
    {
        Assert.Throws<InvalidOperationException>((Action)(() =>
            V9TrainBlockingCandidateDiscovery.Discover(Corpus(), SmallOptions() with { PairSamplingAttemptCap = 1 })));
    }

    [Test]
    public void Enumeration_exposes_all_unions_above_legacy_budget_when_explicitly_configured()
    {
        var features = Enumerable.Range(1, 16).Select(x => "feature" + x).ToArray();
        var observations = new[]
        {
            new BlockingFeatureObservation(true, features.ToDictionary(x => x, _ => (bool?)true)),
            new BlockingFeatureObservation(false, features.ToDictionary(x => x, _ => (bool?)false))
        };
        var options = new BlockingRuleSetSearchOptions(1, 4, 16);
        Assert.Throws<InvalidOperationException>((Action)(() =>
            BlockingRuleSetSearch.EnumerateCandidateRuleSets(observations, features, options)));
        var result = BlockingRuleSetSearch.EnumerateCandidateRuleSets(observations, features,
            options with { CandidateRuleSetCap = 4096, RequireExhaustivePrimitivePool = true });
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.Candidates, Has.Count.EqualTo(2516));
            Assert.That(result.ExhaustivePrimitivePool, Is.True);
        }));
        Assert.Throws<InvalidOperationException>((Action)(() =>
            BlockingRuleSetSearch.EnumerateCandidateRuleSets(observations, features,
                options with { PrimitivePoolSize = 8, RequireExhaustivePrimitivePool = true })));
    }

    private static V9TrainBlockingDiscoveryOptions SmallOptions() => new(
        NonMatchSampleSize: 100,
        SearchOptions: new BlockingRuleSetSearchOptions(1, 2, 4, CandidateRuleSetCap: 100_000));

    private static string[] Signatures(V9TrainBlockingDiscoveryResult result) =>
        result.Candidates.Select(c => string.Join("||", c.Select(p => p.PassId + ":" + string.Join("+", p.Fields)))).ToArray();

    private static V9PhysicalCalibrationObservation[] Corpus() =>
    [
        new(1, "p1", "TRAIN", "a", "Ana Silva", "Maria Silva", new DateOnly(1990, 1, 1)),
        new(2, "p1", "TRAIN", "b", "Ana Xavier", null, new DateOnly(1991, 2, 2)),
        new(3, "p2", "TRAIN", "c", "Bruno Costa", "Joana Costa", new DateOnly(1980, 3, 3)),
        new(4, "p2", "TRAIN", "d", "Completely Different", null, null)
    ];
}
