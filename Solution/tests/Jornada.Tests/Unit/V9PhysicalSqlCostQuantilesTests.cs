using Jornada.Linkage.Parameters.Worker;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9PhysicalSqlCostQuantilesTests
{
    [Test]
    public void NearestRank_P95_P99_Uses_Measured_Values()
    {
        var result = V9PhysicalSqlCostQuantiles.FromMeasuredMilliseconds(
            Enumerable.Range(1, 100).Reverse().Select(x => (double)x));
        Assert.That(result.SampleCount, Is.EqualTo(100));
        Assert.That(result.P95Milliseconds, Is.EqualTo(95));
        Assert.That(result.P99Milliseconds, Is.EqualTo(99));
        Assert.That(result.MaximumMilliseconds, Is.EqualTo(100));
    }

    [Test]
    public void Rejects_Missing_And_Invalid_Timings()
    {
        Assert.That((Action)(() => V9PhysicalSqlCostQuantiles.FromMeasuredMilliseconds([])),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9PhysicalSqlCostQuantiles.FromMeasuredMilliseconds([double.NaN])),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9PhysicalSqlCostQuantiles.FromMeasuredMilliseconds([-1d])),
            Throws.TypeOf<InvalidDataException>());
    }
    [Test]
    public void Repeated_materialization_quantiles_use_one_run_per_sample()
    {
        var runs = Enumerable.Range(1, 100)
            .Select(x => new V9PhysicalSqlMeasuredTimings(1, 2, 3, x, 5));
        var result = V9PhysicalSqlCostQuantiles.FromRepeatedMaterializationRuns(runs);
        Assert.That(result.SampleCount, Is.EqualTo(100));
        Assert.That(result.P95Milliseconds, Is.EqualTo(95));
        Assert.That(result.P99Milliseconds, Is.EqualTo(99));
    }

    [Test]
    public void Repeated_materialization_at_minimum_sample_count_has_p99_equal_to_max()
    {
        var runs = Enumerable.Range(1, 20)
            .Select(x => new V9PhysicalSqlMeasuredTimings(1, 2, 3, x, 5));
        var result = V9PhysicalSqlCostQuantiles.FromRepeatedMaterializationRuns(runs);
        Assert.That(result.SampleCount, Is.EqualTo(20));
        Assert.That(result.P95Milliseconds, Is.EqualTo(19));
        Assert.That(result.P99Milliseconds, Is.EqualTo(20));
        Assert.That(result.MaximumMilliseconds, Is.EqualTo(20));
    }

    [Test]
    public void Repeated_materialization_rejects_insufficient_or_invalid_runs()
    {
        var few = Enumerable.Range(1, 19)
            .Select(x => new V9PhysicalSqlMeasuredTimings(1, 2, 3, x, 5));
        Assert.That((Action)(() => V9PhysicalSqlCostQuantiles.FromRepeatedMaterializationRuns(few)),
            Throws.TypeOf<InvalidDataException>());
        var invalid = Enumerable.Range(1, 20)
            .Select(x => new V9PhysicalSqlMeasuredTimings(1, 2, double.NaN, x, 5));
        Assert.That((Action)(() => V9PhysicalSqlCostQuantiles.FromRepeatedMaterializationRuns(invalid)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Comparable_runs_require_identical_frame_ruleset_and_environment()
    {
        var runs = Enumerable.Range(1, 20)
            .Select(x => new V9PhysicalSqlCostQuantiles.ProvenancedRun(
                "frame-sha", "ruleset-sha", "sql-server-ci",
                new V9PhysicalSqlMeasuredTimings(1, 2, 3, x, 5)))
            .ToArray();

        var result = V9PhysicalSqlCostQuantiles.FromComparableMaterializationRuns(runs);
        Assert.That(result.P95Milliseconds, Is.EqualTo(19));

        var differentFrame = runs.ToArray();
        differentFrame[19] = differentFrame[19] with { FrameFingerprint = "another-frame" };
        Assert.That((Action)(() => V9PhysicalSqlCostQuantiles.FromComparableMaterializationRuns(differentFrame)),
            Throws.TypeOf<InvalidDataException>());

        var differentRuleset = runs.ToArray();
        differentRuleset[19] = differentRuleset[19] with { SqlRulesetFingerprint = "another-ruleset" };
        Assert.That((Action)(() => V9PhysicalSqlCostQuantiles.FromComparableMaterializationRuns(differentRuleset)),
            Throws.TypeOf<InvalidDataException>());

        var differentEnvironment = runs.ToArray();
        differentEnvironment[19] = differentEnvironment[19] with { EnvironmentFingerprint = "other-sql" };
        Assert.That((Action)(() => V9PhysicalSqlCostQuantiles.FromComparableMaterializationRuns(differentEnvironment)),
            Throws.TypeOf<InvalidDataException>());

        var missingProvenance = runs.ToArray();
        missingProvenance[19] = missingProvenance[19] with { FrameFingerprint = "" };
        Assert.That((Action)(() => V9PhysicalSqlCostQuantiles.FromComparableMaterializationRuns(missingProvenance)),
            Throws.TypeOf<InvalidDataException>());
    }

}
