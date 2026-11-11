using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9MarginFrameWorkPartitionTests
{
    private static V9MarginFrameWorkKey Row(string option, decimal margin,
        bool positiveTf = true, int policy = 1, string frame = "three_pass_100k") =>
        new(frame, option, margin, positiveTf, policy);

    [Test]
    public void Official_324_hypotheses_remain_distinct_but_share_four_physical_fit_groups()
    {
        // TF0 x 2 lambdas, 80 positive TF x 2 lambdas, each at 2 margins.
        var rows = new List<V9MarginFrameWorkKey>();
        foreach (var margin in new[] { .5m, 2m })
        {
            for (var lambda = 0; lambda < 2; lambda++)
            {
                rows.Add(Row("TF0_L" + lambda, margin, false, 0));
                for (var tf = 1; tf <= 80; tf++)
                    rows.Add(Row("TF" + tf.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + "_L" + lambda, margin));
            }
        }

        Assert.That(rows, Has.Count.EqualTo(324));
        var groups = V9MarginFrameWorkPartition.Group(rows);
        Assert.That(groups, Has.Count.EqualTo(4),
            "Two margins now reuse the same 3 positive-TF and 1 TF0 SQL/fit groups.");
        Assert.That(groups.Select(g => g.Length).OrderBy(x => x).ToArray(),
            Is.EqualTo(new[] { 4, 64, 128, 128 }));
        Assert.That(groups.SelectMany(g => g).OrderBy(i => i).ToArray(),
            Is.EqualTo(Enumerable.Range(0, 324).ToArray()),
            "Never drop or duplicate one declared hypothesis.");
        foreach (var indices in groups)
        {
            var group = indices.Select(i => rows[i]).ToArray();
            Assert.That(group.Select(x => x.OptionId).Distinct().Count(),
                Is.LessThanOrEqualTo(64));
            Assert.That(group.Select(x => x.Margin).Distinct().Count(),
                Is.LessThanOrEqualTo(8));
            Assert.That(group.Select(x => x.PhysicalSqlFrameKey).Distinct().Count(), Is.EqualTo(1));
            Assert.That(group.Select(x => x.PositiveTf).Distinct().Count(), Is.EqualTo(1));
            Assert.That(group.Select(x => x.UnpublishedTermPolicy).Distinct().Count(), Is.EqualTo(1));
            var expected = group.Select(x => x.OptionId).Distinct().Count()
                * group.Select(x => x.Margin).Distinct().Count();
            Assert.That(group, Has.Length.EqualTo(expected),
                "Every grouped probe must contain the complete declared option×margin lattice.");
        }
    }

    [Test]
    public void Sparse_adaptive_margin_sets_do_not_invent_unrequested_configurations()
    {
        V9MarginFrameWorkKey[] rows =
        [
            Row("A", .5m), Row("A", 2m),
            Row("B", .5m), Row("C", 2m),
            Row("D", .5m, frame: "different_passes"),
            Row("E", .5m, false, 0),
            Row("F", .5m, true, 0)
        ];
        var groups = V9MarginFrameWorkPartition.Group(rows);
        Assert.That(groups.SelectMany(g => g).OrderBy(i => i),
            Is.EqualTo(Enumerable.Range(0, rows.Length)));
        Assert.That(groups, Has.Count.EqualTo(6),
            "Different margin subsets, blocking and TF policy must remain isolated.");
        foreach (var group in groups)
        {
            var present = group.Select(i => rows[i]).ToHashSet();
            foreach (var option in present.Select(x => x.OptionId).Distinct())
            foreach (var margin in present.Select(x => x.Margin).Distinct())
                Assert.That(present.Any(x => x.OptionId == option && x.Margin == margin), Is.True,
                    "No inferred Cartesian configuration may be passed to the physical probe.");
        }
    }

    [Test]
    public void Supports_more_than_eight_margins_by_bounded_slices()
    {
        var rows = Enumerable.Range(0, 9).Select(i => Row("ONE", i + .5m)).ToArray();
        var groups = V9MarginFrameWorkPartition.Group(rows);
        Assert.That(groups.Select(g => g.Length).OrderBy(x => x).ToArray(),
            Is.EqualTo(new[] { 1, 8 }));
        Assert.That(groups.SelectMany(g => g).OrderBy(x => x),
            Is.EqualTo(Enumerable.Range(0, 9)));
    }

    [Test]
    public void Rejects_duplicate_and_invalid_work_or_unsafe_batch_limits()
    {
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9MarginFrameWorkPartition.Group([Row("A", .5m), Row("A", .5m)])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9MarginFrameWorkPartition.Group([Row("A", -1m)])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9MarginFrameWorkPartition.Group([Row("", .5m)])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9MarginFrameWorkPartition.Group([Row("A", .5m)], maximumOptions: 65)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9MarginFrameWorkPartition.Group([Row("A", .5m)], maximumMargins: 9)));
    }
}
