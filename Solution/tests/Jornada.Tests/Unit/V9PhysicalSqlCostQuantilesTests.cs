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

}
