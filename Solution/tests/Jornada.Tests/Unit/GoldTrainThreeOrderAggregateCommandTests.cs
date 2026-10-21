using System.Text.Json;
using System.Text.Json.Nodes;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class GoldTrainThreeOrderAggregateCommandTests
{
    private static void Multiple(Action a) => Assert.Multiple(a);
    private static void Reject<T>(Action a) where T : Exception => Assert.Throws<T>(a);
    private static string Sha(int value) => value.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);

    private static byte[] Report(string orderId, int runIndex, int partitionIndex = 10,
        int corpus = 1, int pending = 2, int? claimedPending = null,
        bool testUsed = false, bool truthExported = false, int falseAccepted = 0,
        int trueAccepted = 2, int falsePairs = 0)
    {
        var waves = GoldTrainThreeOrderProtocol.ResolveOrder(
            orderId, ["G1", "G3", "G2", "G0"]);
        var n = 6;
        var published = n - pending;
        var report = new
        {
            version = "V9_REAL_INCREMENTAL_RUNNER_GOLD_TRAIN_SELECTION_V1",
            snapshotSha256 = Sha(2),
            observations = n,
            identityGroups = 3,
            goldThreeOrderTrial = new
            {
                protocol = GoldTrainThreeOrderProtocol.Version,
                orderId,
                executedWaveOrder = waves,
                sourceKeyContract = GoldTrainThreeOrderProtocol.StableSourceKeyContract,
                originalCorpusSha256 = Sha(corpus),
                frozenSnapshotSha256 = Sha(2),
                isolatedRunFingerprintSha256 = Sha(runIndex + 100),
                elapsedMilliseconds = 1000 + runIndex,
                stablePartitionSha256 = Sha(partitionIndex),
                allFourWavesPublished = true,
                sourceCount = n,
                publishedSourceCount = published,
                evidenceScope = "TRAIN_SELECTION",
                truthExported, uuidExported = false, testUsed,
                independentlyCertified = false,
                activated = false
            },
            goldEndToEnd = new
            {
                Observations = n, PublishedSources = published, PendingSources = pending,
                TrueIdentities = 3, GoldReferences = published > 0 ? 2 : 0,
                PendingSourceRate = (double)pending / n,
                FragmentedTrueIdentities = 1,
                ImpureGoldReferences = 0,
                FalseGoldObservationPairs = falsePairs,
                FinalGoldPairRecall = (double?)0.7,
                FinalGoldPairPrecision = (double?)0.9
            },
            goldBcubed = new
            {
                PublishedSources = published,
                PendingSources = pending,
                GoldIdentityGroups = published > 0 ? 2 : 0,
                StandardPrecision = .8,
                StandardRecall = .7,
                CoverageAwarePrecision = .6,
                CoverageAwareRecall = .5
            },
            pendingReasonBreakdown = new[] { new {
                RawStatus = "NAO_RESOLVIDO",
                RawReasonCategory = "ABAIXO_T_LINKAGE",
                PublicationReasonCategory = "LINKAGE_INDEFINIDO",
                Sources = claimedPending ?? pending } },
            publication = new
            {
                pendingSources = pending,
                trueExistingLinks = trueAccepted,
                falseExistingLinks = falseAccepted,
                fdr = trueAccepted + falseAccepted == 0
                    ? (double?)null : (double)falseAccepted / (trueAccepted + falseAccepted)
            }
        };
        return JsonSerializer.SerializeToUtf8Bytes(report);
    }

    private static byte[][] Three() =>
    [
        Report(GoldTrainThreeOrderProtocol.Baseline, 1),
        Report(GoldTrainThreeOrderProtocol.Reverse, 2),
        Report(GoldTrainThreeOrderProtocol.Interleaved, 3)
    ];

    [Test]
    public void Three_frozen_runs_with_same_partition_produce_aggregate_only()
    {
        var result = GoldTrainThreeOrderAggregateCommand.Compare(Three());
        Multiple(() =>
        {
            Assert.That(result.Orders.Select(x => x.OrderId), Is.EqualTo(GoldTrainThreeOrderProtocol.OrderIds));
            Assert.That(result.SameFinalGoldPartition, Is.True);
            Assert.That(result.LargestPendingDifference, Is.Zero);
            Assert.That(result.LargestFalseGoldPairDifference, Is.Zero);
            Assert.That(result.OriginalCorpusSha256, Is.EqualTo(Sha(1)));
            Assert.That(result.Orders.All(x => x.PendingSources == 2), Is.True);
            Assert.That(result.IdentityLevelEvidenceExported, Is.False);
            Assert.That(result.TestUsed, Is.False);
            Assert.That(result.OperationallyActivated, Is.False);
            Assert.That(result.IndependentlyCertified, Is.False);
        });
        var json = JsonSerializer.Serialize(result);
        Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("TruthBySource"));
            Assert.That(json, Does.Not.Contain("FinalPublishedGoldBySource"));
            Assert.That(json, Does.Not.Contain("pessoa_uuid"));
        });
    }

    private static byte[] WithPolicy(byte[] report, decimal? threshold)
    {
        var json = JsonNode.Parse(report)!.AsObject();
        json["lowerNonMatchExperiment"] = new JsonObject
        {
            ["policy"] = threshold.HasValue ? "EXPERIMENTAL_LOWER_TRAIN_ONLY" : "CURRENT",
            ["threshold"] = threshold.HasValue ? JsonValue.Create(threshold.Value) : null,
            ["scorerReused"] = true,
            ["operationalActivation"] = false,
            ["publicationReason"] = threshold.HasValue
                ? V9GoldLowerNonMatchExperiment.PublicationReason : null,
            ["finalLowerBandProposals"] = 0,
            ["finalLowerBandCreated"] = 0
        };
        return JsonSerializer.SerializeToUtf8Bytes(json);
    }

    [Test]
    public void Three_orders_must_share_identical_policy_and_lower_threshold()
    {
        var same = Three().Select(x => WithPolicy(x, .2m)).ToArray();
        var comparison = GoldTrainThreeOrderAggregateCommand.Compare(same);
        Assert.Multiple(() =>
        {
            Assert.That(comparison.LowerNonMatchPolicy, Is.EqualTo("EXPERIMENTAL_LOWER_TRAIN_ONLY"));
            Assert.That(comparison.LowerNonMatchThreshold, Is.EqualTo(.2m));
        });
        var mixed = (byte[][])same.Clone();
        mixed[2] = WithPolicy(Three()[2], .3m);
        Reject<InvalidDataException>(() => GoldTrainThreeOrderAggregateCommand.Compare(mixed));
        mixed[2] = WithPolicy(Three()[2], null);
        Reject<InvalidDataException>(() => GoldTrainThreeOrderAggregateCommand.Compare(mixed));
    }

    [Test]
    public void Matrix_aggregates_twelve_runs_and_rejects_mixed_or_replayed_experiments()
    {
        var reports = new List<byte[]>();
        for (var policy = 0; policy < 4; policy++)
        {
            for (var order = 0; order < 3; order++)
            {
                var source = Report(GoldTrainThreeOrderProtocol.OrderIds[order], 1 + policy * 3 + order);
                reports.Add(WithPolicy(source, policy == 0 ? null : policy / 10m));
            }
        }
        var matrix = GoldTrainLowerNonMatchMatrixCommand.Compare(reports);
        Assert.Multiple(() =>
        {
            Assert.That(matrix.Rows, Has.Count.EqualTo(12));
            Assert.That(matrix.Rows[0].PolicySlot, Is.EqualTo("CURRENT"));
            Assert.That(matrix.Rows[3].PolicySlot, Is.EqualTo("KNEE"));
            Assert.That(matrix.Rows[6].PolicySlot, Is.EqualTo("CONSERVATIVE"));
            Assert.That(matrix.Rows[9].PolicySlot, Is.EqualTo("LOWEST_PENDING"));
            Assert.That(matrix.Rows[9].LowerThreshold, Is.EqualTo(.3m));
            Assert.That(matrix.Rows.All(x => x.ElapsedMilliseconds > 0), Is.True);
            Assert.That(matrix.Rows.All(x => x.PendingReasons.Sum(y => y.Sources) == x.Metrics.PendingSources), Is.True);
            Assert.That(matrix.OperationallyActivated, Is.False);
        });
        var duplicate = reports.ToArray();
        duplicate[6] = duplicate[3];
        Reject<InvalidDataException>(() => GoldTrainLowerNonMatchMatrixCommand.Compare(duplicate));
        var changed = reports.ToArray();
        changed[7] = WithPolicy(Report(GoldTrainThreeOrderProtocol.OrderIds[1], 8), .1m);
        Reject<InvalidDataException>(() => GoldTrainLowerNonMatchMatrixCommand.Compare(changed));
        Reject<InvalidDataException>(() => GoldTrainLowerNonMatchMatrixCommand.Compare(reports.Take(11).ToArray()));
    }

    [Test]
    public void Changed_partition_and_pending_are_visible_without_source_level_export()
    {
        var a = Three();
        a[2] = Report(GoldTrainThreeOrderProtocol.Interleaved, 3,
            partitionIndex: 99, pending: 3, falsePairs: 2);
        var result = GoldTrainThreeOrderAggregateCommand.Compare(a);
        Multiple(() =>
        {
            Assert.That(result.SameFinalGoldPartition, Is.False);
            Assert.That(result.LargestPendingDifference, Is.EqualTo(1));
            Assert.That(result.LargestFalseGoldPairDifference, Is.EqualTo(2));
            Assert.That(result.Orders[2].GoldReferences, Is.EqualTo(2));
        });
    }

    [Test]
    public void Wrong_corpus_repeated_run_missing_order_or_inconsistent_pending_fail_closed()
    {
        var differentCorpus = Three();
        differentCorpus[1] = Report(GoldTrainThreeOrderProtocol.Reverse, 2, corpus: 99);
        Reject<InvalidDataException>(() => GoldTrainThreeOrderAggregateCommand.Compare(differentCorpus));
        var repeatedRun = Three();
        repeatedRun[1] = Report(GoldTrainThreeOrderProtocol.Reverse, 1);
        Reject<InvalidDataException>(() => GoldTrainThreeOrderAggregateCommand.Compare(repeatedRun));
        var duplicateOrder = Three();
        duplicateOrder[1] = Report(GoldTrainThreeOrderProtocol.Baseline, 2);
        Reject<InvalidDataException>(() => GoldTrainThreeOrderAggregateCommand.Compare(duplicateOrder));
        var inconsistent = Three();
        inconsistent[1] = Report(GoldTrainThreeOrderProtocol.Reverse, 2, claimedPending: 1);
        Reject<InvalidDataException>(() => GoldTrainThreeOrderAggregateCommand.Compare(inconsistent));
    }

    [Test]
    public void Frozen_train_requirements_reject_test_exposure_and_false_fdr()
    {
        var test = Three();
        test[0] = Report(GoldTrainThreeOrderProtocol.Baseline, 1, testUsed: true);
        Reject<InvalidDataException>(() => GoldTrainThreeOrderAggregateCommand.Compare(test));
        var leaked = Three();
        leaked[1] = Report(GoldTrainThreeOrderProtocol.Reverse, 2, truthExported: true);
        Reject<InvalidDataException>(() => GoldTrainThreeOrderAggregateCommand.Compare(leaked));
        var invalidFdr = Three();
        invalidFdr[2] = Report(GoldTrainThreeOrderProtocol.Interleaved, 3, falseAccepted: 5);
        Reject<InvalidDataException>(() => GoldTrainThreeOrderAggregateCommand.Compare(invalidFdr));
    }

    [Test]
    public async Task Cli_reads_only_three_separate_aggregate_reports_and_never_overwrites()
    {
        var root = Path.Combine(Path.GetTempPath(), "gold-agg-" + Guid.NewGuid().ToString("N"));
        var files = Enumerable.Range(0, 3).Select(n =>
            Path.Combine(root, "trial-" + n, GoldTrainThreeOrderAggregateCommand.SourceReportName)).ToArray();
        try
        {
            var data = Three();
            for (var n = 0; n < 3; n++)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(files[n])!);
                await File.WriteAllBytesAsync(files[n], data[n]);
            }
            var output = Path.Combine(root, "public-aggregates.json");
            var args = new[] { GoldTrainThreeOrderAggregateCommand.Flag, files[0], files[1], files[2], output };
            var result = await GoldTrainThreeOrderAggregateCommand.RunAsync(args);
            Multiple(() =>
            {
                Assert.That(result.SameFinalGoldPartition, Is.True);
                Assert.That(File.Exists(output), Is.True);
            });
            var content = await File.ReadAllTextAsync(output);
            Multiple(() =>
            {
                Assert.That(content, Does.Contain("LargestPendingDifference"));
                Assert.That(content, Does.Not.Contain("TruthBySource"));
                Assert.That(content, Does.Not.Contain("FinalPublishedGoldBySource"));
            });
            var threw = false;
            try { await GoldTrainThreeOrderAggregateCommand.RunAsync(args); }
            catch (InvalidDataException) { threw = true; }
            Assert.That(threw, Is.True);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Cli_cannot_compare_a_report_with_itself_as_three_trials()
    {
        var one = Path.Combine(Path.GetTempPath(), "gold-report-" + Guid.NewGuid().ToString("N"),
            GoldTrainThreeOrderAggregateCommand.SourceReportName);
        var rejected = false;
        try
        {
            await GoldTrainThreeOrderAggregateCommand.RunAsync([
                GoldTrainThreeOrderAggregateCommand.Flag, one, one, one, one + ".out"]);
        }
        catch (InvalidDataException) { rejected = true; }
        Assert.That(rejected, Is.True);
    }
}
