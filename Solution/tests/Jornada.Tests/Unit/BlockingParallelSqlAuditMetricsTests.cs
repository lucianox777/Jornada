using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class BlockingParallelSqlAuditMetricsTests
{
    [Test]
    public void Summarize_ComparesD_CAndUnionOverEveryLabeledObservation()
    {
        var rows = new[]
        {
            new BlockingParallelSqlAuditRow(true, 10, 2, 11, 1, true, true, 9, 12, 23),
            new BlockingParallelSqlAuditRow(true, 5, 3, 7, 1, true, false, 7, 10, 15),
            new BlockingParallelSqlAuditRow(false, 2, 0, 2, 0, false, false, 6, 0, 12),
            new BlockingParallelSqlAuditRow(true, 0, 3, 3, 0, false, true, 8, 11, 18)
        };
        var report = BlockingParallelSqlAuditMetrics.Summarize(rows);

        Assert.Multiple(() =>
        {
            Assert.That(report.MethodVersion, Is.EqualTo("BLOCKING_PARALLEL_SQL_AUDIT_V1"));
            Assert.That(report.SampleSize, Is.EqualTo(4));
            Assert.That(report.CombinedEligibleObservations, Is.EqualTo(3));
            Assert.That(report.DynamicCandidatePairs, Is.EqualTo(17));
            Assert.That(report.CombinedCandidatePairs, Is.EqualTo(8));
            Assert.That(report.UnionCandidatePairs, Is.EqualTo(23));
            Assert.That(report.SharedCandidatePairs, Is.EqualTo(2));
            Assert.That(report.DynamicTruthRecovered, Is.EqualTo(2));
            Assert.That(report.CombinedTruthRecovered, Is.EqualTo(2));
            Assert.That(report.UnionTruthRecovered, Is.EqualTo(3));
            Assert.That(report.SharedTruthRecovered, Is.EqualTo(1));
            Assert.That(report.DynamicOnlyTruthRecovered, Is.EqualTo(1));
            Assert.That(report.CombinedOnlyTruthRecovered, Is.EqualTo(1));
            Assert.That(report.DynamicRecallPct, Is.EqualTo(50m));
            Assert.That(report.CombinedRecallPct, Is.EqualTo(50m));
            Assert.That(report.CombinedConditionalRecallPct, Is.EqualTo(66.6667m));
            Assert.That(report.UnionRecallPct, Is.EqualTo(75m));
            Assert.That(report.DynamicLatencyP95Ms, Is.EqualTo(9));
            Assert.That(report.CombinedLatencyP95Ms, Is.EqualTo(12));
            Assert.That(report.TaggedUnionLatencyP95Ms, Is.EqualTo(23));
        });
    }

    [Test]
    public void Summarize_CombinedIneligibleStaysInTotalRecallDenominator()
    {
        var report = BlockingParallelSqlAuditMetrics.Summarize(new[]
        {
            new BlockingParallelSqlAuditRow(false, 1, 0, 1, 0, true, false, 2, 0, 3),
            new BlockingParallelSqlAuditRow(true, 2, 1, 2, 1, true, true, 4, 6, 7)
        });
        Assert.Multiple(() =>
        {
            Assert.That(report.SampleSize, Is.EqualTo(2));
            Assert.That(report.CombinedEligibleObservations, Is.EqualTo(1));
            Assert.That(report.CombinedRecallPct, Is.EqualTo(50m));
            Assert.That(report.CombinedConditionalRecallPct, Is.EqualTo(100m));
            Assert.That(report.UnionRecallPct, Is.EqualTo(100m));
        });
    }

    [Test]
    public void Summarize_RejectsInvalidOverlapAndIneligibleCombinedResults()
    {
        Assert.That(() => BlockingParallelSqlAuditMetrics.Summarize(new[]
        {
            new BlockingParallelSqlAuditRow(true, 2, 2, 5, 1, false, false, 1, 1, 1)
        }), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => BlockingParallelSqlAuditMetrics.Summarize(new[]
        {
            new BlockingParallelSqlAuditRow(false, 0, 1, 1, 0, false, true, 1, 1, 1)
        }), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void OptionalSqlAuditFlag_RequiresExplicitBooleanAndDefaultsOff()
    {
        var option = BlockingPassAuditCommand.CompareCombinedOption;
        Assert.Multiple(() =>
        {
            Assert.That(BlockingPassAuditCommand.ReadOptionalBooleanOption([], option), Is.False);
            Assert.That(BlockingPassAuditCommand.ReadOptionalBooleanOption([option, "true"], option), Is.True);
            Assert.That(BlockingPassAuditCommand.ReadOptionalBooleanOption([option + "=true"], option), Is.True);
            Assert.That(BlockingPassAuditCommand.ReadOptionalBooleanOption([option, "false"], option), Is.False);
            Assert.That(BlockingPassAuditCommand.ReadOptionalBooleanOption([option + "=false"], option), Is.False);
        });
        Assert.That(() => BlockingPassAuditCommand.ReadOptionalBooleanOption([option], option),
            Throws.TypeOf<ArgumentException>());
        Assert.That(() => BlockingPassAuditCommand.ReadOptionalBooleanOption([option + "=maybe"], option),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Summarize_ZeroEvidenceIsExplicit()
    {
        var report = BlockingParallelSqlAuditMetrics.Summarize(Array.Empty<BlockingParallelSqlAuditRow>());
        Assert.Multiple(() =>
        {
            Assert.That(report.SampleSize, Is.Zero);
            Assert.That(report.CombinedEligibleObservations, Is.Zero);
            Assert.That(report.UnionRecallPct, Is.Zero);
            Assert.That(report.TaggedUnionLatencyP95Ms, Is.Zero);
        });
    }
}
