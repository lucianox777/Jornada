using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class GoldTrainThreeOrderProtocolTests
{
    private static void Multiple(Action a) => Assert.Multiple(a);
    private static void Reject<T>(Action a) where T : Exception => Assert.Throws<T>(a);
    private static string Sha(int n) => n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);

    private static Dictionary<long, string> Truth() => new()
    {
        [1] = "P", [2] = "P", [3] = "Q", [4] = "Q", [5] = "R"
    };

    private static Dictionary<long, Guid> Final(bool mixed = false)
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        return mixed
            ? new() { [1] = a, [3] = a, [2] = b, [4] = b, [5] = c }
            : new() { [1] = a, [2] = a, [3] = b, [4] = b, [5] = c };
    }

    private static GoldTrainThreeOrderProtocol.Trial Trial(string id, int runNumber,
        bool mixed = false, Dictionary<long, Guid>? published = null) =>
        new(id, Sha(11), Sha(12), Sha(100 + runNumber), "TRAIN_SELECTION",
            TestUsed: false, Activated: false, AllFourWavesPublished: true,
            GoldTrainThreeOrderProtocol.ResolveOrder(id, ["G3", "G2", "G1", "G0"]),
            Truth(), published ?? Final(mixed));

    private static GoldTrainThreeOrderProtocol.Trial[] Three(bool reverseMixed = false) =>
    [
        Trial(GoldTrainThreeOrderProtocol.Baseline, 1),
        Trial(GoldTrainThreeOrderProtocol.Reverse, 2, mixed: reverseMixed),
        Trial(GoldTrainThreeOrderProtocol.Interleaved, 3)
    ];

    [Test]
    public void Three_orders_are_predeclared_and_independent_of_input_enumeration()
    {
        Multiple(() =>
        {
            Assert.That(GoldTrainThreeOrderProtocol.ResolveOrder(GoldTrainThreeOrderProtocol.Baseline,
                ["G3", "G1", "G2", "G0"]), Is.EqualTo(new[] { "G0", "G1", "G2", "G3" }));
            Assert.That(GoldTrainThreeOrderProtocol.ResolveOrder(GoldTrainThreeOrderProtocol.Reverse,
                ["G0", "G2", "G3", "G1"]), Is.EqualTo(new[] { "G3", "G2", "G1", "G0" }));
            Assert.That(GoldTrainThreeOrderProtocol.ResolveOrder(GoldTrainThreeOrderProtocol.Interleaved,
                ["G0", "G1", "G2", "G3"]), Is.EqualTo(new[] { "G1", "G3", "G0", "G2" }));
        });
        Reject<InvalidDataException>(() =>
            GoldTrainThreeOrderProtocol.ResolveOrder("BEST_METRICS_ORDER", ["G0", "G1", "G2", "G3"]));
        Reject<InvalidDataException>(() =>
            GoldTrainThreeOrderProtocol.ResolveOrder(GoldTrainThreeOrderProtocol.Baseline, ["G0", "G0", "G2", "G3"]));
        Reject<InvalidDataException>(() =>
            GoldTrainThreeOrderProtocol.ResolveOrder(GoldTrainThreeOrderProtocol.Baseline, ["G0", "G1", "G2"]));
    }

    [Test]
    public void Identical_truth_partition_with_different_random_uuids_is_order_invariant()
    {
        var report = GoldTrainThreeOrderProtocol.Compare(Three());
        Multiple(() =>
        {
            Assert.That(report.Version, Is.EqualTo(GoldTrainThreeOrderProtocol.Version));
            Assert.That(report.Orders.Select(x => x.OrderId),
                Is.EqualTo(new[] { GoldTrainThreeOrderProtocol.Baseline,
                    GoldTrainThreeOrderProtocol.Reverse, GoldTrainThreeOrderProtocol.Interleaved }));
            Assert.That(report.GoldPartitionOrderInvariant, Is.True);
            Assert.That(report.LargestPendingDifference, Is.Zero);
            Assert.That(report.LargestFalseGoldPairDifference, Is.Zero);
            Assert.That(report.Orders.All(x => x.PairRecall == 1 && x.PairPrecision == 1), Is.True);
            Assert.That(report.Orders.All(x => x.BcubedCoverageAwareRecall == 1), Is.True);
            Assert.That(report.IncrementalLinkageFdrComputed, Is.False);
            Assert.That(report.IndependentlyCertified, Is.False);
            Assert.That(report.TestUsed, Is.False);
            Assert.That(report.OperationallyActivated, Is.False);
        });
    }

    [Test]
    public void Detects_worse_partition_and_avoids_claiming_fdr_from_pair_precision()
    {
        var report = GoldTrainThreeOrderProtocol.Compare(Three(reverseMixed: true));
        var baseline = report.Orders.Single(x => x.OrderId == GoldTrainThreeOrderProtocol.Baseline);
        var reverse = report.Orders.Single(x => x.OrderId == GoldTrainThreeOrderProtocol.Reverse);
        Multiple(() =>
        {
            Assert.That(report.GoldPartitionOrderInvariant, Is.False);
            Assert.That(baseline.TruePairs, Is.EqualTo(2));
            Assert.That(baseline.CorrectGoldPairs, Is.EqualTo(2));
            Assert.That(reverse.CorrectGoldPairs, Is.Zero);
            Assert.That(reverse.FalseGoldPairs, Is.EqualTo(2));
            Assert.That(reverse.ImpureGoldReferences, Is.EqualTo(2));
            Assert.That(report.LargestFalseGoldPairDifference, Is.EqualTo(2));
            Assert.That(reverse.PairRecall, Is.Zero);
            Assert.That(reverse.PairPrecision, Is.Zero);
            Assert.That(report.IncrementalLinkageFdrComputed, Is.False);
        });
    }

    [Test]
    public void Aggregates_do_not_prove_equal_partition_even_if_quality_numbers_match()
    {
        var runs = Three();
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        // Both have exactly one published true P pair and two pending sources,
        // but WHICH source is pending is different.
        runs[0] = runs[0] with { FinalPublishedGoldBySource =
            new Dictionary<long, Guid> { [1] = x, [2] = x, [3] = y } };
        var q = Guid.NewGuid();
        runs[1] = runs[1] with { FinalPublishedGoldBySource =
            new Dictionary<long, Guid> { [3] = q, [4] = q, [1] = Guid.NewGuid() } };
        var report = GoldTrainThreeOrderProtocol.Compare(runs);
        Assert.That(report.GoldPartitionOrderInvariant, Is.False);
    }

    [Test]
    public void Rejects_unfrozen_fits_untagged_test_partial_orders_and_repeat_sql_runs()
    {
        var a = Three();
        a[1] = a[1] with { FrozenSnapshotSha256 = Sha(99) };
        Reject<InvalidDataException>(() => GoldTrainThreeOrderProtocol.Compare(a));
        a = Three();
        a[1] = a[1] with { OriginalCorpusSha256 = Sha(99) };
        Reject<InvalidDataException>(() => GoldTrainThreeOrderProtocol.Compare(a));
        a = Three();
        a[1] = a[1] with { IsolatedRunFingerprintSha256 = a[0].IsolatedRunFingerprintSha256 };
        Reject<InvalidDataException>(() => GoldTrainThreeOrderProtocol.Compare(a));
        a = Three();
        a[1] = a[1] with { TestUsed = true };
        Reject<InvalidDataException>(() => GoldTrainThreeOrderProtocol.Compare(a));
        a = Three();
        a[1] = a[1] with { EvidenceScope = "VALIDATION" };
        Reject<InvalidDataException>(() => GoldTrainThreeOrderProtocol.Compare(a));
        a = Three();
        a[1] = a[1] with { AllFourWavesPublished = false };
        Reject<InvalidDataException>(() => GoldTrainThreeOrderProtocol.Compare(a));
        a = Three();
        a[1] = a[1] with { ExecutedWaveOrder = new[] { "G0", "G1", "G2", "G3" } };
        Reject<InvalidDataException>(() => GoldTrainThreeOrderProtocol.Compare(a));
        a = Three();
        a[1] = a[1] with { TruthBySource = new Dictionary<long, string> { [1] = "P" } };
        Reject<InvalidDataException>(() => GoldTrainThreeOrderProtocol.Compare(a));
        a = Three();
        a[1] = a[1] with { FinalPublishedGoldBySource = new Dictionary<long, Guid> { [900] = Guid.NewGuid() } };
        Reject<InvalidDataException>(() => GoldTrainThreeOrderProtocol.Compare(a));
    }

    [Test]
    public void A_pending_observation_stays_in_all_metric_denominators()
    {
        var runs = Three();
        runs[0] = runs[0] with { FinalPublishedGoldBySource =
            new Dictionary<long, Guid> { [1] = Guid.NewGuid(), [2] = Guid.NewGuid() } };
        var report = GoldTrainThreeOrderProtocol.Compare(runs);
        Multiple(() =>
        {
            Assert.That(report.Orders[0].Sources, Is.EqualTo(5));
            Assert.That(report.Orders[0].PendingSources, Is.EqualTo(3));
            Assert.That(report.Orders[0].TruePairs, Is.EqualTo(2));
            Assert.That(report.Orders[0].CorrectGoldPairs, Is.Zero);
            Assert.That(report.Orders[0].BcubedCoverageAwareRecall, Is.LessThan(1));
            Assert.That(report.LargestPendingDifference, Is.EqualTo(3));
        });
    }
}
