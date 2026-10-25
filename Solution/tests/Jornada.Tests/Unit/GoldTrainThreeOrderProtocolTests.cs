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
            GoldTrainThreeOrderProtocol.StableSourceKeyContract,
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
    public void Stable_key_comes_from_actual_csv_observation_id_not_sql_or_row_position()
    {
        var id = "P000102-G3-1";
        var k = GoldTrainThreeOrderProtocol.StableKeyFromCsvObservationId(id);
        Multiple(() =>
        {
            Assert.That(k, Is.GreaterThan(0L));
            Assert.That(GoldTrainThreeOrderProtocol.StableKeyFromCsvObservationId(id), Is.EqualTo(k));
            Assert.That(GoldTrainThreeOrderProtocol.StableKeyFromCsvObservationId("P000102-G0-1"),
                Is.Not.EqualTo(k));
            Assert.That(GoldTrainThreeOrderProtocol.StableKeyFromCsvObservationId(id + "-2"),
                Is.Not.EqualTo(k));
        });
        Reject<InvalidDataException>(() => GoldTrainThreeOrderProtocol.StableKeyFromCsvObservationId(""));
        Reject<InvalidDataException>(() => GoldTrainThreeOrderProtocol.StableKeyFromCsvObservationId("   "));
    }

    [Test]
    public void Stable_partition_digest_is_invariant_to_random_gold_uuids_and_source_iteration_order()
    {
        var gold1 = new Dictionary<long, Guid>();
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        gold1[1] = g1;
        gold1[2] = g1;
        gold1[3] = g2;
        var gold2 = new Dictionary<long, Guid>();
        var h2 = Guid.NewGuid();
        var h1 = Guid.NewGuid();
        gold2[3] = h2;
        gold2[2] = h1;
        gold2[1] = h1;
        var truth = Truth();
        var reversed = truth.Reverse().ToDictionary(x => x.Key, x => x.Value);
        var first = GoldTrainThreeOrderProtocol.FingerprintStablePartition(truth, gold1);
        var second = GoldTrainThreeOrderProtocol.FingerprintStablePartition(reversed, gold2);
        var modified = GoldTrainThreeOrderProtocol.FingerprintStablePartition(
            truth, new Dictionary<long, Guid>
            {
                [1] = h1, [2] = h2, [3] = h2
            });
        var pendingChanged = GoldTrainThreeOrderProtocol.FingerprintStablePartition(
            truth, new Dictionary<long, Guid> { [1] = h1, [2] = h1 });
        Multiple(() =>
        {
            Assert.That(first.Length, Is.EqualTo(64));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(modified, Is.Not.EqualTo(first));
            Assert.That(pendingChanged, Is.Not.EqualTo(first));
        });
    }

    [Test]
    public void Stable_digest_rejects_sql_generated_keys_outside_train_or_invalid_source()
    {
        var truth = Truth();
        Reject<InvalidDataException>(() => GoldTrainThreeOrderProtocol.FingerprintStablePartition(
            truth, new Dictionary<long, Guid> { [999] = Guid.NewGuid() }));
        Reject<InvalidDataException>(() => GoldTrainThreeOrderProtocol.FingerprintStablePartition(
            truth, new Dictionary<long, Guid> { [1] = Guid.Empty }));
        Reject<InvalidDataException>(() => GoldTrainThreeOrderProtocol.FingerprintStablePartition(
            new Dictionary<long, string> { [-1] = "P" }, new Dictionary<long, Guid>()));
        Reject<InvalidDataException>(() => GoldTrainThreeOrderProtocol.FingerprintStablePartition(
            new Dictionary<long, string>(), new Dictionary<long, Guid>()));
    }

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
        a[1] = a[1] with { SourceKeyContract = "SQL_IDENTITY_OBSERVACAO" };
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
    private static GoldTrainLowerNonMatchMatrixCommand.Matrix FrozenKneeFixture()
    {
        var rows = new List<GoldTrainLowerNonMatchMatrixCommand.Row>();
        foreach (var slot in new[] { "CURRENT", "KNEE" })
        {
            var knee = slot == "KNEE";
            for (var i = 0; i < GoldTrainThreeOrderProtocol.OrderIds.Count; i++)
            {
                var pending = knee ? 26 + i : 2788 + i;
                var metrics = new GoldTrainThreeOrderAggregateCommand.OrderMetric(
                    OrderId: GoldTrainThreeOrderProtocol.OrderIds[i],
                    StablePartitionSha256: Sha(100 + rows.Count),
                    Sources: 9240,
                    TrueIdentities: 5003,
                    PublishedSources: 9240 - pending,
                    PendingSources: pending,
                    PendingSourceRate: pending / 9240.0,
                    GoldReferences: 5003,
                    FragmentedTrueIdentities: knee ? 5 : 2,
                    ImpureGoldReferences: i == 1 ? 0 : 1,
                    FalseGoldPairs: i == 0 ? 3 : i == 1 ? 0 : 6,
                    PairRecall: 0.9, PairPrecision: 0.99,
                    BcubedStandardPrecision: 0.999, BcubedStandardRecall: 0.995,
                    BcubedCoverageAwarePrecision: knee ? 0.997 : 0.70,
                    BcubedCoverageAwareRecall: knee ? 0.995 : 0.69,
                    CorrectAcceptedLinks: 1000, FalseAcceptedLinks: i == 1 ? 0 : 3,
                    AcceptedLinkFdr: i == 1 ? 0 : 0.003);
                rows.Add(new GoldTrainLowerNonMatchMatrixCommand.Row(
                    slot,
                    knee ? "EXPERIMENTAL_LOWER_TRAIN_ONLY" : "CURRENT",
                    knee ? 0.05m : null,
                    metrics,
                    60_000,
                    15,
                    knee ? 10 : 0,
                    knee ? 4 + i : 1,
                    knee ? 4 + i : 0,
                    Array.Empty<GoldTrainLowerNonMatchMatrixCommand.PendingReason>()));
            }
        }
        return new GoldTrainLowerNonMatchMatrixCommand.Matrix(
            "GOLD_TRAIN_LOWER_NONMATCH_MATRIX_V2", Sha(900), Sha(901),
            rows, TrainOnly: true, IndependentlyCertified: false,
            OperationallyActivated: false)
        {
            FinalistCount = 1, ExecutedTrialCount = 6
        };
    }

    [Test]
    public void Gold_V6_knee_can_be_frozen_only_for_new_validation_without_implicit_activation()
    {
        var candidate = GoldTrainThreeOrderProtocol.FreezeKneeForIndependentValidation(
            FrozenKneeFixture(), 0.05m, Sha(902), Sha(903), 20261025001);
        Multiple(() =>
        {
            Assert.That(candidate.ContractVersion, Is.EqualTo("GOLD_V6_FROZEN_KNEE_VALIDATION_CANDIDATE_V1"));
            Assert.That(candidate.CandidateLowerThreshold, Is.EqualTo(0.05m));
            Assert.That(candidate.TrainingCorpusSha256, Is.EqualTo(Sha(900)));
            Assert.That(candidate.GoldFittedSnapshotSha256, Is.EqualTo(Sha(901)));
            Assert.That(candidate.ProposedHoldoutSeed, Is.EqualTo(20261025001UL));
            Assert.That(candidate.SourceCount, Is.EqualTo(9240));
            Assert.That(candidate.IndependentTrainingIdentityGroups, Is.EqualTo(5003));
            Assert.That(candidate.WorstPendingSources, Is.EqualTo(28));
            Assert.That(candidate.WorstFalseNewIdentityCreations, Is.EqualTo(6));
            Assert.That(candidate.WorstFalseGoldPairs, Is.EqualTo(6));
            Assert.That(candidate.WorstCoverageAwareBcubedF1, Is.GreaterThan(0.99));
            Assert.That(candidate.IndependentlyValidated, Is.False);
            Assert.That(candidate.TestUsed, Is.False);
            Assert.That(candidate.Certified, Is.False);
            Assert.That(candidate.OperationallyActivated, Is.False);
        });
    }

    [Test]
    public void Gold_V6_freeze_rejects_retuning_history_missing_evidence_and_untrusted_metrics()
    {
        var good = FrozenKneeFixture();
        void Freeze(GoldTrainLowerNonMatchMatrixCommand.Matrix matrix,
            decimal threshold = 0.05m, ulong seed = 20261025001,
            string? matrixSha = null)
        {
            _ = GoldTrainThreeOrderProtocol.FreezeKneeForIndependentValidation(
                matrix, threshold, Sha(902), matrixSha ?? Sha(903), seed);
        }
        Reject<InvalidDataException>(() => Freeze(good, threshold: 0.5m));
        foreach (var usedSeed in new ulong[] { 0, 42, 43, 44, 20261019001 })
            Reject<InvalidDataException>(() => Freeze(good, seed: usedSeed));
        Reject<InvalidDataException>(() => Freeze(good, matrixSha: "invalid"));
        Reject<InvalidDataException>(() => Freeze(good with { TrainOnly = false }));
        Reject<InvalidDataException>(() => Freeze(good with { IndependentlyCertified = true }));
        Reject<InvalidDataException>(() => Freeze(good with { OperationallyActivated = true }));
        Reject<InvalidDataException>(() => Freeze(good with { ExecutedTrialCount = 3 }));
        Reject<InvalidDataException>(() => Freeze(good with { FinalistCount = 0 }));
        Reject<InvalidDataException>(() => Freeze(good with
        {
            Rows = good.Rows.Take(5).ToArray()
        }));
        var rows = good.Rows.ToArray();
        rows[3] = rows[3] with { LowerThreshold = 0.5m };
        Reject<InvalidDataException>(() => Freeze(good with { Rows = rows }));
        rows = good.Rows.ToArray();
        rows[5] = rows[5] with { Metrics = rows[5].Metrics with { AcceptedLinkFdr = 1.1 } };
        Reject<InvalidDataException>(() => Freeze(good with { Rows = rows }));
        rows = good.Rows.ToArray();
        rows[2] = rows[2] with { Metrics = rows[2].Metrics with { Sources = 9999 } };
        Reject<InvalidDataException>(() => Freeze(good with { Rows = rows }));
        rows = good.Rows.ToArray();
        rows[5] = rows[5] with { Metrics = rows[5].Metrics with { OrderId = GoldTrainThreeOrderProtocol.Baseline } };
        Reject<InvalidDataException>(() => Freeze(good with { Rows = rows }));
    }

}
