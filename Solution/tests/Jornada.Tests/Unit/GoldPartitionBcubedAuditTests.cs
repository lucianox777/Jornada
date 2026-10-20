using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class GoldPartitionBcubedAuditTests
{
    private static void Multiple(Action a) => Assert.Multiple(a);
    private static void Reject<T>(Action a) where T : Exception => Assert.Throws<T>(a);

    [Test]
    public void Mixed_gold_with_pending_counts_every_source_and_penalizes_nonpublication()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var metric = GoldPartitionBcubedAudit.Measure(
            new Dictionary<long, string>
            {
                [1] = "A", [2] = "A", [3] = "A",
                [4] = "B", [5] = "B", [6] = "C"
            },
            new Dictionary<long, Guid>
            {
                [1] = a, [2] = a, [4] = a,
                [3] = b, [5] = b
            });
        Multiple(() =>
        {
            Assert.That(metric.ContractVersion, Is.EqualTo(GoldPartitionBcubedAudit.Version));
            Assert.That(metric.Sources, Is.EqualTo(6));
            Assert.That(metric.TrueIdentityGroups, Is.EqualTo(3));
            Assert.That(metric.GoldIdentityGroups, Is.EqualTo(2));
            Assert.That(metric.PublishedSources, Is.EqualTo(5));
            Assert.That(metric.PendingSources, Is.EqualTo(1));
            Assert.That(metric.PendingSourceRate, Is.EqualTo(1d / 6d).Within(1e-12));
            Assert.That(metric.StandardPrecision, Is.EqualTo(11d / 18d).Within(1e-12));
            Assert.That(metric.StandardRecall, Is.EqualTo(11d / 18d).Within(1e-12));
            Assert.That(metric.StandardF1, Is.EqualTo(11d / 18d).Within(1e-12));
            Assert.That(metric.CoverageAwarePrecision, Is.EqualTo(4d / 9d).Within(1e-12));
            Assert.That(metric.CoverageAwareRecall, Is.EqualTo(4d / 9d).Within(1e-12));
            Assert.That(metric.CoverageAwareF1, Is.EqualTo(4d / 9d).Within(1e-12));
            Assert.That(metric.PublishedOnlyPrecision, Is.EqualTo(8d / 15d).Within(1e-12));
            Assert.That(metric.PublishedOnlyRecall, Is.EqualTo(8d / 15d).Within(1e-12));
        });
    }

    [Test]
    public void Entirely_pending_truth_never_gets_perfect_coverage_aware_quality()
    {
        var metrics = GoldPartitionBcubedAudit.Measure(
            new Dictionary<long, string> { [1] = "A", [2] = "A" },
            new Dictionary<long, Guid>());
        Multiple(() =>
        {
            Assert.That(metrics.StandardPrecision, Is.EqualTo(1));
            Assert.That(metrics.StandardRecall, Is.EqualTo(.5));
            Assert.That(metrics.CoverageAwarePrecision, Is.Zero);
            Assert.That(metrics.CoverageAwareRecall, Is.Zero);
            Assert.That(metrics.CoverageAwareF1, Is.Zero);
            Assert.That(metrics.PublishedOnlyPrecision, Is.Null);
            Assert.That(metrics.PublishedOnlyRecall, Is.Null);
            Assert.That(metrics.PendingSources, Is.EqualTo(2));
        });
    }

    [Test]
    public void Singleton_pending_can_artificially_make_standard_bcubed_perfect()
    {
        var metrics = GoldPartitionBcubedAudit.Measure(
            new Dictionary<long, string> { [1] = "A", [2] = "B" },
            new Dictionary<long, Guid> { [1] = Guid.NewGuid() });
        Multiple(() =>
        {
            Assert.That(metrics.StandardPrecision, Is.EqualTo(1));
            Assert.That(metrics.StandardRecall, Is.EqualTo(1));
            Assert.That(metrics.CoverageAwarePrecision, Is.EqualTo(.5));
            Assert.That(metrics.CoverageAwareRecall, Is.EqualTo(.5));
            Assert.That(metrics.PendingSourceRate, Is.EqualTo(.5));
        });
    }

    [Test]
    public void Two_references_for_one_person_keep_precision_but_lose_recall()
    {
        var metrics = GoldPartitionBcubedAudit.Measure(
            new Dictionary<long, string> { [1] = "A", [2] = "A" },
            new Dictionary<long, Guid> { [1] = Guid.NewGuid(), [2] = Guid.NewGuid() });
        Multiple(() =>
        {
            Assert.That(metrics.CoverageAwarePrecision, Is.EqualTo(1));
            Assert.That(metrics.CoverageAwareRecall, Is.EqualTo(.5));
            Assert.That(metrics.CoverageAwareF1, Is.EqualTo(2d / 3d).Within(1e-12));
        });
    }

    [Test]
    public void Two_people_fused_into_one_reference_lose_precision()
    {
        var uuid = Guid.NewGuid();
        var metrics = GoldPartitionBcubedAudit.Measure(
            new Dictionary<long, string> { [1] = "A", [2] = "B" },
            new Dictionary<long, Guid> { [1] = uuid, [2] = uuid });
        Multiple(() =>
        {
            Assert.That(metrics.CoverageAwarePrecision, Is.EqualTo(.5));
            Assert.That(metrics.CoverageAwareRecall, Is.EqualTo(1));
            Assert.That(metrics.GoldIdentityGroups, Is.EqualTo(1));
        });
    }

    [Test]
    public void No_foreign_sources_empty_guid_empty_truth_or_empty_dataset()
    {
        Reject<InvalidDataException>(() => GoldPartitionBcubedAudit.Measure(
            new Dictionary<long, string>(), new Dictionary<long, Guid>()));
        Reject<InvalidDataException>(() => GoldPartitionBcubedAudit.Measure(
            new Dictionary<long, string> { [1] = "" }, new Dictionary<long, Guid>()));
        Reject<InvalidDataException>(() => GoldPartitionBcubedAudit.Measure(
            new Dictionary<long, string> { [1] = "A" },
            new Dictionary<long, Guid> { [2] = Guid.NewGuid() }));
        Reject<InvalidDataException>(() => GoldPartitionBcubedAudit.Measure(
            new Dictionary<long, string> { [1] = "A" },
            new Dictionary<long, Guid> { [1] = Guid.Empty }));
    }
}
