using System.Numerics;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9IntegralSearchTests
{
    [Test]
    public void Exact_quotient_reconciles_every_subset_against_brute_edge_unions_without_labels()
    {
        var passes = Enumerable.Range(0, 6).Select(i => new LinkageBlockingPass("P" + i, ["f" + i])).ToArray();
        var space = new V9DeclaredBlockingSearchSpace(passes);
        BigInteger[] edges = [1, 3, 5, 8, 12, 20, 33, 49];
        string Union(IReadOnlyList<LinkageBlockingPass> selection)
        {
            var mask = selection.Aggregate(BigInteger.Zero, (m, p) => m | (BigInteger.One << Array.FindIndex(passes, x => x.PassId == p.PassId)));
            return string.Join(',', edges.Select((m, i) => (m, i)).Where(x => (x.m & mask) != 0).Select(x => x.i));
        }
        var brute = space.Enumerate().GroupBy(Union).ToDictionary(x => x.Key, x => x.LongCount());
        var actual = V9CandidateFrameEquivalence.Partition(space, edges, CancellationToken.None);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(actual.ConfigurationsEnumerated, Is.EqualTo("63"));
            Assert.That(actual.Classes.Sum(x => x.MemberCount), Is.EqualTo(63));
            Assert.That(actual.Classes.Count, Is.EqualTo(brute.Count));
            Assert.That(actual.AllEquivalentPhysicalLatenciesMeasured, Is.False);
            Assert.That(actual.FunctionalCandidateSpaceExhaustive, Is.True);
            foreach (var group in actual.Classes)
                Assert.That(group.MemberCount, Is.EqualTo(brute[Union(group.Representative)]));
        }));
        var reordered = V9CandidateFrameEquivalence.Partition(space, edges.Reverse().Concat(edges), CancellationToken.None);
        Assert.That(reordered, Is.EqualTo(actual).UsingPropertiesComparer());
        var empty = V9CandidateFrameEquivalence.Partition(space, [], CancellationToken.None);
        Assert.That(empty.Classes.Single().MemberCount, Is.EqualTo(63));
        Assert.That((Action)(() => V9CandidateFrameEquivalence.Partition(space, [64], CancellationToken.None)), Throws.TypeOf<InvalidDataException>());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.That((Action)(() => V9CandidateFrameEquivalence.Partition(space, edges, cancelled.Token)), Throws.TypeOf<OperationCanceledException>());
    }

    [Test]
    public void Catalogue_enumerates_all_4095_subsets_including_twelve_passes_without_pool_pruning()
    {
        var catalogue = Enumerable.Range(0, 12).Select(i => new LinkageBlockingPass("P" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), ["f" + i])).ToArray();
        var space = new V9DeclaredBlockingSearchSpace(catalogue);
        var all = space.Enumerate().ToArray();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(space.Count(), Is.EqualTo(new BigInteger(4095)));
            Assert.That(all.Length, Is.EqualTo(4095));
            Assert.That(all[^1].Count, Is.EqualTo(12));
            Assert.That(all.Select(x => string.Join('|', x.Select(p => p.PassId))).Distinct().Count(), Is.EqualTo(4095));
            Assert.That(new V9DeclaredBlockingSearchSpace(catalogue.Reverse().ToArray()).FingerprintSha256,
                Is.EqualTo(space.FingerprintSha256));
            Assert.That(new V9DeclaredBlockingSearchSpace(catalogue, 3, 12).Count(), Is.EqualTo(new BigInteger(4017)));
        }));
        Assert.DoesNotThrow((Action)(() => new BlockingRuleSetSearchOptions(12, 12, 5000, CandidateRuleSetCap: 2_000_000).Validate()));
    }

    [Test]
    public void Huge_catalogue_counts_exactly_and_enumerates_lazily_with_immutable_results()
    {
        var catalogue = Enumerable.Range(0, 64).Select(i => new LinkageBlockingPass("P" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), ["f" + i])).ToArray();
        var space = new V9DeclaredBlockingSearchSpace(catalogue);
        var frozen = space.Enumerate();
        var sha = space.FingerprintSha256;
        catalogue[0] = new("MUTATED", ["changed"]);
        Assert.That(frozen.First().Single().PassId, Is.EqualTo("P00"));
        Assert.That(space.FingerprintSha256, Is.Not.EqualTo(sha));
        Assert.That(space.Count(), Is.EqualTo(BigInteger.Pow(2, 64) - 1));
        Assert.That((Action)(() => new V9DeclaredBlockingSearchSpace([catalogue[0], catalogue[0]]).Count()),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public async Task Parallel_map_really_overlaps_work_but_preserves_order_and_worker_bound()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0; var maximum = 0;
        var task = V9ParallelWork.MapAsync(Enumerable.Range(0, 8).ToArray(), 2, async (value, ct) =>
        {
            var current = Interlocked.Increment(ref active);
            if (current == 2) { Interlocked.Exchange(ref maximum, 2); entered.TrySetResult(); }
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            await Task.Yield();
            Interlocked.Decrement(ref active);
            return value * value;
        }, CancellationToken.None);
        var result = await task;
        Assert.That(maximum, Is.EqualTo(2));
        Assert.That(result, Is.EqualTo(new[] { 0, 1, 4, 9, 16, 25, 36, 49 }));
        Assert.That(active, Is.Zero);
    }

    [Test]
    public void Risk_envelopes_report_best_recall_per_band_without_forging_eligibility()
    {
        var safe = new V9BootstrapPareto2D.CandidateOption("SAFE", new string('a', 64), .9, 0, 10, 90, 0, 100,
            100, true, true, .003, 10);
        var unsafeEvidence = safe with { OptionId = "UNATTESTED", ConfigurationFingerprintSha256 = new string('b', 64),
            GlobalRecall = .99, TrueAccepted = 99, IdentityClusterUncertaintyAttested = false, IdentityClusterFdrUpper = .008 };
        var exploratory = safe with { OptionId = "EXPLORATORY", ConfigurationFingerprintSha256 = new string('c', 64),
            GlobalRecall = 1, TrueAccepted = 100, FalseAccepted = 1, AcceptedFdr = 1d / 101, IdentityClusterFdrUpper = .02 };
        var report = V9RiskBandFrontier.Evaluate("VALIDATION", false, [exploratory, unsafeEvidence, safe],
            [.03, .001, .005, .01], new(.5, .01, 2));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.Bands.Select(b => b.BestConditionalOptionId), Is.EqualTo(new string?[] { null, "SAFE", "UNATTESTED", "EXPLORATORY" }));
            Assert.That(report.Bands.Select(b => b.BestEligibleOptionId), Is.EqualTo(new string?[] { null, "SAFE", "SAFE", null }));
            Assert.That(report.Bands[2].ConditionalExclusionReasons, Does.Contain("EVIDENCIA_ESTATISTICA_INSUFICIENTE"));
            Assert.That(report.Bands[3].ConditionalExclusionReasons, Does.Contain("FAIXA_EXPLORATORIA_ACIMA_DO_RISCO_OPERACIONAL"));
            Assert.That(report.AutomaticActivationAllowed, Is.False);
        }));
        Assert.That((Action)(() => V9RiskBandFrontier.Evaluate("TEST", false, [safe], [.01], new(.5, .01, 2))), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9RiskBandFrontier.Evaluate("VALIDATION", false, [safe], [.01, .01], new(.5, .01, 2))), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Sample_plan_prices_rare_cells_power_risk_bands_and_multiplicity_separately()
    {
        var design = new V9StatisticalSamplePlan.Design(.05, .8, .95, .97, .01, .02, .001, 81, 100,
            4, 1.3, .2, .2, .9, 1.3);
        var result = V9StatisticalSamplePlan.Calculate(design, [.01, .001]);
        var moreModels = V9StatisticalSamplePlan.Calculate(design with { ComparedConfigurations = 10000 }, [.01, .001]);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.RiskBands[0].ValidationIndependentUnitsZeroError,
                Is.GreaterThan(result.RiskBands[1].ValidationIndependentUnitsZeroError!.Value));
            Assert.That(moreModels.RecallPowerIndependentUnits, Is.GreaterThan(result.RecallPowerIndependentUnits));
            Assert.That(result.TrainingIndependentPairsPerClassForAnyCellObservation, Is.GreaterThan(1000));
            Assert.That(result.RequiredBaseUnits, Is.GreaterThan(0));
            Assert.That(result.Status, Does.StartWith("CONDITIONAL"));
        }));
        Assert.That((Action)(() => V9StatisticalSamplePlan.Calculate(design with { RecallAlternative = .94 }, [.01])),
            Throws.TypeOf<InvalidDataException>());
    }
}
