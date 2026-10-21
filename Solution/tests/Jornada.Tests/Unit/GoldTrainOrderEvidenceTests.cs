using System.Text.Json;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class GoldTrainOrderEvidenceTests
{
    private static void Multiple(Action a) => Assert.Multiple(a);
    private static void Reject<T>(Action a) where T : Exception => Assert.Throws<T>(a);
    private static string Sha(int n) => n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);

    private static GoldTrainThreeOrderProtocol.Trial Trial(
        string order, int run, bool alternateSqlIds = false)
    {
        var offset = alternateSqlIds ? 1000L : 100L;
        var map = new Dictionary<long, GoldTrainOrderEvidence.SourceCrosswalk>
        {
            [offset + 10] = new(1, "P"),
            [offset + 20] = new(2, "P"),
            [offset + 30] = new(3, "Q"),
            [offset + 40] = new(4, "Q")
        };
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        var gold = new Dictionary<long, Guid>
        {
            [offset + 10] = x, [offset + 20] = x,
            [offset + 30] = y, [offset + 40] = y
        };
        var plan = GoldTrainThreeOrderProtocol.ResolveOrder(order,
            ["G3", "G1", "G0", "G2"]);
        return GoldTrainOrderEvidence.Create(order, Sha(11), Sha(12), Sha(100 + run),
            plan, map, gold);
    }

    [Test]
    public void Reversing_sql_identity_allocation_preserves_csv_keys_and_pair_membership()
    {
        var baseline = Trial(GoldTrainThreeOrderProtocol.Baseline, 1);
        var reversed = Trial(GoldTrainThreeOrderProtocol.Reverse, 2, true);
        var interleaved = Trial(GoldTrainThreeOrderProtocol.Interleaved, 3, true);
        var result = GoldTrainThreeOrderProtocol.Compare(
            [baseline, reversed, interleaved]);
        Multiple(() =>
        {
            Assert.That(baseline.TruthBySource.Keys.Order(), Is.EqualTo(new long[] { 1, 2, 3, 4 }));
            Assert.That(reversed.FinalPublishedGoldBySource.Keys.Order(), Is.EqualTo(new long[] { 1, 2, 3, 4 }));
            Assert.That(baseline.SourceKeyContract, Is.EqualTo(GoldTrainThreeOrderProtocol.StableSourceKeyContract));
            Assert.That(result.GoldPartitionOrderInvariant, Is.True);
            Assert.That(result.LargestPendingDifference, Is.Zero);
            Assert.That(result.Orders.All(x => x.PairRecall == 1), Is.True);
            Assert.That(result.IncrementalLinkageFdrComputed, Is.False);
            Assert.That(result.TestUsed, Is.False);
            Assert.That(result.OperationallyActivated, Is.False);
        });
    }

    [Test]
    public void Crosswalk_rejects_duplicated_csv_source_unknown_sql_uuid_and_wrong_wave_order()
    {
        var map = new Dictionary<long, GoldTrainOrderEvidence.SourceCrosswalk>
        {
            [100] = new(1, "P"), [200] = new(1, "Q")
        };
        var order = GoldTrainThreeOrderProtocol.ResolveOrder(
            GoldTrainThreeOrderProtocol.Baseline, ["G0", "G1", "G2", "G3"]);
        Reject<InvalidDataException>(() => GoldTrainOrderEvidence.Create(
            GoldTrainThreeOrderProtocol.Baseline, Sha(1), Sha(2), Sha(3), order,
            map, new Dictionary<long, Guid>()));
        map[200] = new(2, "Q");
        Reject<InvalidDataException>(() => GoldTrainOrderEvidence.Create(
            GoldTrainThreeOrderProtocol.Baseline, Sha(1), Sha(2), Sha(3), order,
            map, new Dictionary<long, Guid> { [300] = Guid.NewGuid() }));
        Reject<InvalidDataException>(() => GoldTrainOrderEvidence.Create(
            GoldTrainThreeOrderProtocol.Baseline, Sha(1), Sha(2), Sha(3),
            ["G3", "G2", "G1", "G0"], map, new Dictionary<long, Guid>()));
        Reject<InvalidDataException>(() => GoldTrainOrderEvidence.Create(
            GoldTrainThreeOrderProtocol.Baseline, Sha(1), Sha(2), Sha(3), order,
            map, new Dictionary<long, Guid> { [100] = Guid.Empty }));
    }

    [Test]
    public async Task Offline_command_reads_private_trials_and_exports_aggregates_without_labels_or_uuids()
    {
        var root = Path.Combine(Path.GetTempPath(), "gold-three-" + Guid.NewGuid().ToString("N"));
        var dirs = Enumerable.Range(0, 3).Select(x => Path.Combine(root, "trial-" + x)).ToArray();
        Directory.CreateDirectory(root);
        try
        {
            var trials = new[]
            {
                Trial(GoldTrainThreeOrderProtocol.Baseline, 1),
                Trial(GoldTrainThreeOrderProtocol.Reverse, 2, true),
                Trial(GoldTrainThreeOrderProtocol.Interleaved, 3, true)
            };
            var names = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                Directory.CreateDirectory(dirs[i]);
                var path = Path.Combine(dirs[i], GoldTrainThreeOrderCompareCommand.PrivateEvidenceFileName);
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(trials[i]));
                names.Add(path);
            }
            var output = Path.Combine(root, "aggregate.json");
            var args = new[] { GoldTrainThreeOrderCompareCommand.Flag,
                names[0], names[1], names[2], output };
            var report = await GoldTrainThreeOrderCompareCommand.RunAsync(args);
            Multiple(() =>
            {
                Assert.That(report.GoldPartitionOrderInvariant, Is.True);
                Assert.That(File.Exists(output), Is.True);
            });
            var json = await File.ReadAllTextAsync(output);
            Multiple(() =>
            {
                Assert.That(json, Does.Contain("LargestPendingDifference"));
                Assert.That(json, Does.Not.Contain("TruthBySource"));
                Assert.That(json, Does.Not.Contain("FinalPublishedGoldBySource"));
                Assert.That(json, Does.Not.Contain("00000000-"));
            });
            // No overwrite of an existing report.
            var rejected = false;
            try { await GoldTrainThreeOrderCompareCommand.RunAsync(args); }
            catch (IOException) { rejected = true; }
            Assert.That(rejected, Is.True);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Comparison_rejects_repeated_input_path_even_before_loading()
    {
        var path = Path.Combine(Path.GetTempPath(), "gold-empty-" + Guid.NewGuid().ToString("N"));
        var bad = false;
        try
        {
            await GoldTrainThreeOrderCompareCommand.RunAsync([
                GoldTrainThreeOrderCompareCommand.Flag,
                path, path, path, path + "-report"]);
        }
        catch (InvalidDataException) { bad = true; }
        Assert.That(bad, Is.True);
    }
}
