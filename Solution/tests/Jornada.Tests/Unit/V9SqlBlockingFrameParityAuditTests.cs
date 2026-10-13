using Jornada.Contracts;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlBlockingFrameParityAuditTests
{
    private static IReadOnlySet<BlockingProjectionKey> Keys(string a, string b) =>
        new HashSet<BlockingProjectionKey>
        {
            new("a", a), new("b", b)
        };

    private static readonly V9SqlBlockingFrameParityAudit.Pass[] Passes =
    [
        new("P_A", ["a"]),
        new("P_B", ["b"])
    ];

    private static readonly V9SqlBlockingFrameParityAudit.Pair[] Eligible =
    [
        new(0, 1, "TRAIN", Keys("same", "x"), Keys("same", "y")),   // ACTIVE only (a)
        new(2, 3, "TRAIN", Keys("x", "same"), Keys("y", "same")),   // C1 and ACTIVE (b)
        new(4, 5, "TRAIN", Keys("x", "y"), Keys("u", "v")),         // C1 only
        new(6, 7, "TRAIN", Keys("x", "y"), Keys("u", "v")),         // lost
        new(8, 9, "TEST", Keys("same", "same"), Keys("same", "same")) // both ACTIVE passes
    ];

    [Test]
    public void Reports_disjoint_C1_ACTIVE_overlap_and_exact_SQL_parity_on_independent_M_frame()
    {
        var c1 = new HashSet<(int A, int B)> { (2, 3), (4, 5) };
        var sql = new HashSet<(int A, int B)> { (0, 1), (2, 3), (4, 5), (8, 9) };
        var report = V9SqlBlockingFrameParityAudit.Analyze(Eligible, Passes, c1, sql);
        var train = report.Partitions.Single(x => x.Name == "TRAIN");
        var test = report.Partitions.Single(x => x.Name == "TEST");
        var trainA = report.Passes.Single(x => x.Partition == "TRAIN" && x.PassId == "P_A");
        var testA = report.Passes.Single(x => x.Partition == "TEST" && x.PassId == "P_A");

        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.Status, Does.Contain("NOT_CALIBRATOR_GATE"));
            Assert.That(report.Partitions, Has.Count.EqualTo(3));
            Assert.That(report.Passes, Has.Count.EqualTo(6));
            Assert.That(train.EligibleM, Is.EqualTo(4));
            Assert.That(train.C1M, Is.EqualTo(2));
            Assert.That(train.ActiveLogicalM, Is.EqualTo(2));
            Assert.That(train.BothC1AndActiveM, Is.EqualTo(1));
            Assert.That(train.C1OnlyM, Is.EqualTo(1));
            Assert.That(train.ActiveOnlyM, Is.EqualTo(1));
            Assert.That(train.LogicalUnionM, Is.EqualTo(3));
            Assert.That(train.PhysicalSqlUnionM, Is.EqualTo(3));
            Assert.That(train.NotRecoveredM, Is.EqualTo(1));
            Assert.That(trainA.RetainedM, Is.EqualTo(1));
            Assert.That(trainA.ExclusiveOutsideC1M, Is.EqualTo(1));
            Assert.That(test.ActiveLogicalM, Is.EqualTo(1));
            Assert.That(test.LogicalUnionM, Is.EqualTo(1));
            Assert.That(testA.RetainedM, Is.EqualTo(1));
            Assert.That(testA.ExclusiveOutsideC1M, Is.Zero);
        }));
    }

    [Test]
    public void Fails_closed_if_SQL_union_loses_logical_active_match()
    {
        var c1 = new HashSet<(int A, int B)> { (2, 3), (4, 5) };
        var missing = new HashSet<(int A, int B)> { (2, 3), (4, 5), (8, 9) };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlBlockingFrameParityAudit.Analyze(Eligible, Passes, c1, missing)));
    }

    [Test]
    public void Fails_closed_on_physical_SQL_M_outside_same_frame_or_duplicate()
    {
        var c1 = new HashSet<(int A, int B)> { (2, 3), (4, 5) };
        var correct = new HashSet<(int A, int B)> { (0, 1), (2, 3), (4, 5), (8, 9) };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlBlockingFrameParityAudit.Analyze(Eligible, Passes, c1,
                new HashSet<(int A, int B)>(correct) { (10, 11) })));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9SqlBlockingFrameParityAudit.Analyze([Eligible[0], Eligible[0]], Passes,
                new HashSet<(int A, int B)>(), new HashSet<(int A, int B)>())));
    }

    [Test]
    public void Incomplete_pass_does_not_weaken_AND_and_missing_key_is_not_agreement()
    {
        var pair = new V9SqlBlockingFrameParityAudit.Pair(
            0, 1, "VALIDATION",
            new HashSet<BlockingProjectionKey> { new("a", "same") },
            new HashSet<BlockingProjectionKey> { new("a", "same") });
        var compound = new V9SqlBlockingFrameParityAudit.Pass("P_AB", ["a", "b"]);
        var result = V9SqlBlockingFrameParityAudit.Analyze(
            [pair], [compound], new HashSet<(int A, int B)>(),
            new HashSet<(int A, int B)>());
        Assert.That(result.Partitions.Single(x => x.Name == "VALIDATION").NotRecoveredM,
            Is.EqualTo(1));
    }
}
